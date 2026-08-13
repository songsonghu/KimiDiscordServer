using KimiDiscordServer.Bot.Models;

namespace KimiDiscordServer.Bot.Services;

public interface IAiChatClient
{
    string ProviderName { get; }

    Task<string> GenerateReplyAsync(AiChatRequest request, CancellationToken cancellationToken);
}
