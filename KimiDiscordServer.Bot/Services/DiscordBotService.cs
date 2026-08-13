using System.Text;
using Discord;
using Discord.WebSocket;
using KimiDiscordServer.Bot.Configuration;
using KimiDiscordServer.Bot.Models;
using Microsoft.Extensions.Options;

namespace KimiDiscordServer.Bot.Services;

public sealed class DiscordBotService : BackgroundService
{
    private static readonly (string Prefix, string Provider)[] ProviderPrefixes =
    [
        ("/claude", "Claude"),
        ("claude:", "Claude"),
        ("/kimi", "Kimi"),
        ("kimi:", "Kimi")
    ];

    private readonly DiscordSocketClient _client;
    private readonly ILogger<DiscordBotService> _logger;
    private readonly IOptions<DiscordOptions> _discordOptions;
    private readonly IOptions<AiOptions> _aiOptions;
    private readonly AiProviderFactory _providerFactory;
    private readonly AttachmentContentService _attachmentContentService;
    private readonly ChannelExecutionCoordinator _channelExecutionCoordinator;
    private CancellationToken _stoppingToken = CancellationToken.None;

    public DiscordBotService(
        DiscordSocketClient client,
        ILogger<DiscordBotService> logger,
        IOptions<DiscordOptions> discordOptions,
        IOptions<AiOptions> aiOptions,
        AiProviderFactory providerFactory,
        AttachmentContentService attachmentContentService,
        ChannelExecutionCoordinator channelExecutionCoordinator)
    {
        _client = client;
        _logger = logger;
        _discordOptions = discordOptions;
        _aiOptions = aiOptions;
        _providerFactory = providerFactory;
        _attachmentContentService = attachmentContentService;
        _channelExecutionCoordinator = channelExecutionCoordinator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        var options = _discordOptions.Value;
        if (string.IsNullOrWhiteSpace(options.Token))
        {
            throw new InvalidOperationException("Discord bot token is not configured.");
        }

        _client.Log += OnDiscordLogAsync;
        _client.Ready += OnReadyAsync;
        _client.MessageReceived += OnMessageReceivedAsync;

        await _client.LoginAsync(TokenType.Bot, options.Token);
        await _client.StartAsync();

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _client.MessageReceived -= OnMessageReceivedAsync;
            _client.Ready -= OnReadyAsync;
            _client.Log -= OnDiscordLogAsync;

            await _client.LogoutAsync();
            await _client.StopAsync();
        }
    }

    private Task OnReadyAsync()
    {
        _logger.LogInformation("Discord bot connected as {Username}.", _client.CurrentUser.Username);
        return Task.CompletedTask;
    }

    private async Task OnMessageReceivedAsync(SocketMessage rawMessage)
    {
        if (rawMessage is not SocketUserMessage message || message.Author.IsBot)
        {
            return;
        }

        if (!TryPrepareRequest(message, out var providerName, out var cleanedPrompt, out var providerExplicitlySelected))
        {
            return;
        }

        if (providerExplicitlySelected && !_providerFactory.IsProviderConfigured(providerName))
        {
            await message.ReplyAsync(
                $"「{providerName}」未配置 API Key，无法使用该模型。请改用其他已配置的模型，或联系管理员补充配置。",
                allowedMentions: AllowedMentions.None);
            return;
        }

        var semaphore = _channelExecutionCoordinator.Get(message.Channel.Id);
        var acquired = false;
        try
        {
            await semaphore.WaitAsync(_stoppingToken);
            acquired = true;
            using var typing = message.Channel.EnterTypingState();
            var request = await BuildRequestAsync(providerName, cleanedPrompt, message);
            var client = _providerFactory.Resolve(request.Provider);
            var response = await client.GenerateReplyAsync(request, _stoppingToken);

            if (string.IsNullOrWhiteSpace(response))
            {
                response = "模型已收到请求，但没有返回可显示的文本结果。";
            }

            await SendReplyAsync(message, response);
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to process Discord message {MessageId}.", message.Id);
            await message.ReplyAsync(
                "处理请求时发生错误，请检查 Discord Token、模型 API Key 和模型配置。",
                allowedMentions: AllowedMentions.None);
        }
        finally
        {
            if (acquired)
            {
                semaphore.Release();
            }
        }
    }

    private bool TryPrepareRequest(SocketUserMessage message, out string providerName, out string cleanedPrompt, out bool providerExplicitlySelected)
    {
        var options = _discordOptions.Value;
        providerName = _providerFactory.GetDefaultProvider();
        cleanedPrompt = RemoveBotMention(message.Content).Trim();
        providerExplicitlySelected = false;

        var isDirectMessage = message.Channel is IDMChannel;
        if (isDirectMessage && !options.AllowDirectMessages)
        {
            return false;
        }

        if (!isDirectMessage && options.AllowedChannelIds.Count > 0 && !options.AllowedChannelIds.Contains(message.Channel.Id))
        {
            return false;
        }

        var hasExplicitProvider = TryStripProviderPrefix(cleanedPrompt, out var explicitProviderName, out var strippedPrompt);
        if (hasExplicitProvider)
        {
            providerName = explicitProviderName;
            cleanedPrompt = strippedPrompt;
            providerExplicitlySelected = true;
        }
        var mentionsBot = _client.CurrentUser is not null &&
            message.MentionedUsers.Any(user => user.Id == _client.CurrentUser.Id);

        var requiresBotMention = RequiresBotMention(message.Channel, options);

        if (!isDirectMessage && requiresBotMention && !mentionsBot && !hasExplicitProvider)
        {
            return false;
        }

        cleanedPrompt = cleanedPrompt.Trim();
        return !string.IsNullOrWhiteSpace(cleanedPrompt) || message.Attachments.Count > 0;
    }

    private async Task<AiChatRequest> BuildRequestAsync(string providerName, string prompt, SocketUserMessage message)
    {
        var promptParts = new List<AiPromptPart>
        {
            AiPromptPart.TextPart(BuildEnvelopeText(message, prompt))
        };

        var attachmentParts = await _attachmentContentService.BuildPartsAsync(
            message.Attachments,
            _discordOptions.Value,
            _stoppingToken);

        promptParts.AddRange(attachmentParts);

        return new AiChatRequest(
            providerName,
            _aiOptions.Value.SystemPrompt,
            promptParts);
    }

    private string BuildEnvelopeText(SocketUserMessage message, string prompt)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Discord user: {message.Author.Username}");
        builder.AppendLine($"Channel: {DescribeChannel(message.Channel)}");

        if (string.IsNullOrWhiteSpace(prompt))
        {
            builder.AppendLine("User request: Please process the uploaded attachment(s).");
        }
        else
        {
            builder.AppendLine("User request:");
            builder.AppendLine(prompt);
        }

        return builder.ToString();
    }

    private async Task SendReplyAsync(SocketUserMessage sourceMessage, string content)
    {
        var firstChunk = true;
        foreach (var chunk in Chunk(content, _discordOptions.Value.MaxDiscordMessageLength))
        {
            await sourceMessage.Channel.SendMessageAsync(
                text: chunk,
                messageReference: firstChunk ? new MessageReference(sourceMessage.Id) : null,
                allowedMentions: AllowedMentions.None);
            firstChunk = false;
        }
    }

    private static IEnumerable<string> Chunk(string content, int maxLength)
    {
        var remaining = content.Trim();
        while (remaining.Length > maxLength)
        {
            var splitAt = remaining.LastIndexOf('\n', maxLength);
            if (splitAt <= 0)
            {
                splitAt = maxLength;
            }

            yield return remaining[..splitAt].Trim();
            remaining = remaining[splitAt..].Trim();
        }

        if (!string.IsNullOrWhiteSpace(remaining))
        {
            yield return remaining;
        }
    }

    private static string DescribeChannel(ISocketMessageChannel channel) => channel switch
    {
        SocketGuildChannel guildChannel => $"#{guildChannel.Name} ({guildChannel.Guild.Name})",
        IDMChannel => "Direct Message",
        _ => channel.Name ?? channel.Id.ToString()
    };

    private static bool RequiresBotMention(ISocketMessageChannel channel, DiscordOptions options)
    {
        if (!options.RequireBotMention || channel is IDMChannel)
        {
            return false;
        }

        if (options.MentionOptionalChannelIds.Contains(channel.Id))
        {
            return false;
        }

        return channel is SocketGuildChannel guildChannel
            && string.Equals(guildChannel.Name, "general", StringComparison.OrdinalIgnoreCase);
    }

    private static string RemoveBotMention(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        return string.Join(
            ' ',
            content
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(static token => !(token.StartsWith("<@") && token.EndsWith('>'))));
    }

    private static bool TryStripProviderPrefix(string content, out string providerName, out string cleanedPrompt)
    {
        foreach (var (prefix, provider) in ProviderPrefixes)
        {
            if (content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                providerName = provider;
                cleanedPrompt = content[prefix.Length..].TrimStart(' ', ':', '-');
                return true;
            }
        }

        providerName = string.Empty;
        cleanedPrompt = content;
        return false;
    }

    private Task OnDiscordLogAsync(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information
        };

        _logger.Log(level, message.Exception, "[Discord] {Message}", message.Message);
        return Task.CompletedTask;
    }
}
