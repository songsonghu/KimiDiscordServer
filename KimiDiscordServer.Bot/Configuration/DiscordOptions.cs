namespace KimiDiscordServer.Bot.Configuration;

public sealed class DiscordOptions
{
    public const string SectionName = "Discord";

    public string Token { get; set; } = string.Empty;

    public bool RequireBotMention { get; set; } = true;

    public bool AllowDirectMessages { get; set; } = true;

    public List<ulong> AllowedChannelIds { get; set; } = [];

    public int MaxAttachmentBytes { get; set; } = 4 * 1024 * 1024;

    public int MaxTextDocumentBytes { get; set; } = 256 * 1024;

    public int MaxDiscordMessageLength { get; set; } = 1900;
}
