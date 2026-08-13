using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KimiDiscordServer.Bot.Configuration;
using KimiDiscordServer.Bot.Models;
using Microsoft.Extensions.Options;

namespace KimiDiscordServer.Bot.Services;

public sealed class ClaudeAiClient : IAiChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<AiOptions> _options;

    public ClaudeAiClient(IHttpClientFactory httpClientFactory, IOptions<AiOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public string ProviderName => "Claude";

    public async Task<string> GenerateReplyAsync(AiChatRequest request, CancellationToken cancellationToken)
    {
        var options = _options.Value.Claude;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("Claude API key is not configured.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        httpRequest.Headers.Add("x-api-key", options.ApiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = new
        {
            model = options.Model,
            max_tokens = options.MaxOutputTokens,
            system = request.SystemPrompt,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = request.Parts.Select(MapContent).ToArray()
                }
            }
        };

        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await SendAsync(httpRequest, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Claude request failed: {(int)response.StatusCode} {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var textSegments = document.RootElement
            .GetProperty("content")
            .EnumerateArray()
            .Where(static content => content.GetProperty("type").GetString() == "text")
            .Select(content => content.GetProperty("text").GetString())
            .Where(static text => !string.IsNullOrWhiteSpace(text));

        return string.Join(Environment.NewLine, textSegments);
    }

    private static object MapContent(AiPromptPart part)
    {
        return part.Kind switch
        {
            "text" => new
            {
                type = "text",
                text = part.Text
            },
            "image" => new
            {
                type = "image",
                source = new
                {
                    type = "base64",
                    media_type = part.MediaType,
                    data = part.Base64Data
                }
            },
            _ => throw new InvalidOperationException($"Unsupported prompt part '{part.Kind}'.")
        };
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequest, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClientFactory.CreateClient("Claude").SendAsync(httpRequest, cancellationToken);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Claude request timed out.", exception);
        }
    }
}
