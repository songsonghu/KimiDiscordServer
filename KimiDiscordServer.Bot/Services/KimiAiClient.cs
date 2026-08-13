using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KimiDiscordServer.Bot.Configuration;
using KimiDiscordServer.Bot.Models;
using Microsoft.Extensions.Options;

namespace KimiDiscordServer.Bot.Services;

public sealed class KimiAiClient : IAiChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<AiOptions> _options;

    public KimiAiClient(IHttpClientFactory httpClientFactory, IOptions<AiOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public string ProviderName => "Kimi";

    public async Task<string> GenerateReplyAsync(AiChatRequest request, CancellationToken cancellationToken)
    {
        var options = _options.Value.Kimi;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("Kimi API key is not configured.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = new
        {
            model = options.Model,
            max_tokens = options.MaxOutputTokens,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = request.SystemPrompt
                },
                new
                {
                    role = "user",
                    content = request.Parts.Select(MapContent).ToArray()
                }
            }
        };

        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _httpClientFactory.CreateClient().SendAsync(httpRequest, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Kimi request failed: {(int)response.StatusCode} {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var message = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content");

        return message.ValueKind switch
        {
            JsonValueKind.String => message.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join(
                Environment.NewLine,
                message.EnumerateArray()
                    .Where(static item => item.TryGetProperty("type", out var type) && type.GetString() == "text")
                    .Select(item => item.GetProperty("text").GetString())
                    .Where(static text => !string.IsNullOrWhiteSpace(text))),
            _ => string.Empty
        };
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
                type = "image_url",
                image_url = new
                {
                    url = $"data:{part.MediaType};base64,{part.Base64Data}"
                }
            },
            _ => throw new InvalidOperationException($"Unsupported prompt part '{part.Kind}'.")
        };
    }
}
