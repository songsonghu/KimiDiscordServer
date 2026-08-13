using System.Text;
using Discord;
using KimiDiscordServer.Bot.Configuration;
using KimiDiscordServer.Bot.Models;

namespace KimiDiscordServer.Bot.Services;

public sealed class AttachmentContentService
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".json", ".xml", ".yaml", ".yml", ".csv", ".log",
        ".cs", ".js", ".ts", ".tsx", ".jsx", ".py", ".java", ".go", ".sql", ".html", ".css"
    };

    private readonly IHttpClientFactory _httpClientFactory;

    public AttachmentContentService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyList<AiPromptPart>> BuildPartsAsync(
        IReadOnlyCollection<Attachment> attachments,
        DiscordOptions options,
        CancellationToken cancellationToken)
    {
        if (attachments.Count == 0)
        {
            return [];
        }

        var client = _httpClientFactory.CreateClient();
        var parts = new List<AiPromptPart>();

        foreach (var attachment in attachments)
        {
            if (attachment.Size > options.MaxAttachmentBytes)
            {
                parts.Add(AiPromptPart.TextPart(
                    $"Attachment '{attachment.Filename}' was skipped because it exceeds the {options.MaxAttachmentBytes} byte limit."));
                continue;
            }

            var bytes = await client.GetByteArrayAsync(attachment.Url, cancellationToken);
            var mediaType = string.IsNullOrWhiteSpace(attachment.ContentType)
                ? GuessMediaType(attachment.Filename)
                : attachment.ContentType;

            if (IsImage(mediaType))
            {
                parts.Add(AiPromptPart.TextPart(
                    $"Image attachment: {attachment.Filename} ({mediaType}, {attachment.Size} bytes)."));
                parts.Add(AiPromptPart.ImagePart(mediaType, Convert.ToBase64String(bytes)));
                continue;
            }

            if (IsTextDocument(attachment.Filename, mediaType) && bytes.Length <= options.MaxTextDocumentBytes)
            {
                var text = DecodeText(bytes);
                if (!LooksBinary(text))
                {
                    parts.Add(AiPromptPart.TextPart(
                        $"Document '{attachment.Filename}' content:\n{text}"));
                    continue;
                }
            }

            parts.Add(AiPromptPart.TextPart(
                $"Document attachment: {attachment.Filename} ({mediaType}, {attachment.Size} bytes). The file was received but could not be converted into plain text automatically."));
        }

        return parts;
    }

    private static bool IsImage(string mediaType) => mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    private static bool IsTextDocument(string filename, string mediaType)
    {
        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TextExtensions.Contains(Path.GetExtension(filename));
    }

    private static string DecodeText(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    private static bool LooksBinary(string text) => text.Any(static character => character == '\0');

    private static string GuessMediaType(string filename)
    {
        return Path.GetExtension(filename).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            ".json" => "application/json",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
    }
}
