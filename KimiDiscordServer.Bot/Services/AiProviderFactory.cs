using KimiDiscordServer.Bot.Configuration;
using Microsoft.Extensions.Options;

namespace KimiDiscordServer.Bot.Services;

public sealed class AiProviderFactory
{
    private readonly Dictionary<string, IAiChatClient> _clients;
    private readonly AiOptions _options;

    public AiProviderFactory(
        IEnumerable<IAiChatClient> clients,
        IOptions<AiOptions> options)
    {
        _options = options.Value;
        _clients = clients.ToDictionary(client => client.ProviderName, StringComparer.OrdinalIgnoreCase);
    }

    public string GetDefaultProvider() => _options.DefaultProvider;

    public bool IsProviderConfigured(string providerName)
    {
        return providerName switch
        {
            { } p when string.Equals(p, "Claude", StringComparison.OrdinalIgnoreCase) =>
                !string.IsNullOrWhiteSpace(_options.Claude.ApiKey),
            { } p when string.Equals(p, "Kimi", StringComparison.OrdinalIgnoreCase) =>
                !string.IsNullOrWhiteSpace(_options.Kimi.ApiKey),
            _ => true
        };
    }

    public IAiChatClient Resolve(string providerName)
    {
        if (_clients.TryGetValue(providerName, out var client))
        {
            return client;
        }

        throw new InvalidOperationException($"Unsupported AI provider '{providerName}'.");
    }
}
