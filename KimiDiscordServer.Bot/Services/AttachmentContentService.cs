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

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
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

        var httpClient = _httpClientFactory.CreateClient("DiscordAttachments");
        var parts = new List<AiPromptPart>();

        foreach (var attachment in attachments)
        {
            if (attachment.Size > options.MaxAttachmentBytes)
            {
                parts.Add(AiPromptPart.TextPart(
                    $"Attachment '{attachment.Filename}' was skipped because Discord reported a size larger than the {options.MaxAttachmentBytes} byte limit."));
                continue;
            }

            byte[]? bytes;
            try
            {
                bytes = await DownloadBytesAsync(httpClient, attachment, options.MaxAttachmentBytes, cancellationToken);
            }
            catch (HttpRequestException)
            {
                parts.Add(AiPromptPart.TextPart(
                    $"Attachment '{attachment.Filename}' could not be downloaded from Discord and was skipped."));
                continue;
            }

            if (bytes is null)
            {
                parts.Add(AiPromptPart.TextPart(
                    $"Attachment '{attachment.Filename}' was skipped because the downloaded payload exceeded the {options.MaxAttachmentBytes} byte limit."));
                continue;
            }

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

    private static string DecodeText(byte[] bytes) => Utf8.GetString(bytes);

    private static bool LooksBinary(string text) => text.Any(static character => character == '\0');

    private static async Task<byte[]?> DownloadBytesAsync(
        HttpClient httpClient,
        Attachment attachment,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            attachment.Url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long contentLength && contentLength > maxBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memoryStream = new MemoryStream();
        var buffer = new byte[81920];
        long totalRead = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
            if (totalRead > maxBytes)
            {
                return null;
            }

            await memoryStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return memoryStream.ToArray();
    }

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
