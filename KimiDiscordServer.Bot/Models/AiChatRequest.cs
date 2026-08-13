namespace KimiDiscordServer.Bot.Models;

public sealed record AiChatRequest(
    string Provider,
    string SystemPrompt,
    IReadOnlyList<AiPromptPart> Parts);
