using System.Collections.Concurrent;

namespace KimiDiscordServer.Bot.Services;

public sealed class ChannelExecutionCoordinator : IDisposable
{
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _locks = new();

    public SemaphoreSlim Get(ulong channelId) => _locks.GetOrAdd(channelId, static _ => new SemaphoreSlim(1, 1));

    public void Dispose()
    {
        foreach (var semaphore in _locks.Values)
        {
            semaphore.Dispose();
        }
    }
}
