namespace KimiDiscordServer.Bot.Configuration;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public string DefaultProvider { get; set; } = "Claude";

    public string SystemPrompt { get; set; } =
        "You are a helpful assistant replying inside Discord. Keep answers clear, practical, and in the same language as the user's latest request.";

    public ClaudeOptions Claude { get; set; } = new();

    public KimiOptions Kimi { get; set; } = new();
}

public sealed class ClaudeOptions
{
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "claude-3-5-sonnet-latest";

    public string Endpoint { get; set; } = "https://api.anthropic.com/v1/messages";

    public int MaxOutputTokens { get; set; } = 1024;
}

public sealed class KimiOptions
{
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "moonshot-v1-8k";

    public string Endpoint { get; set; } = "https://api.moonshot.cn/v1/chat/completions";

    public int MaxOutputTokens { get; set; } = 1024;
}
