namespace KimiDiscordServer.Bot.Models;

public sealed record AiPromptPart
{
    private AiPromptPart(string kind, string? text = null, string? mediaType = null, string? base64Data = null)
    {
        Kind = kind;
        Text = text;
        MediaType = mediaType;
        Base64Data = base64Data;
    }

    public string Kind { get; }

    public string? Text { get; }

    public string? MediaType { get; }

    public string? Base64Data { get; }

    public static AiPromptPart TextPart(string text) => new("text", text: text);

    public static AiPromptPart ImagePart(string mediaType, string base64Data) =>
        new("image", mediaType: mediaType, base64Data: base64Data);
}
