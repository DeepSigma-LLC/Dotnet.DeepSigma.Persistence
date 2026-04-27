using System.Collections.Concurrent;

namespace DeepSigma.Persistence.InMemory;

/// <summary>
/// Holds the backing store for <see cref="InMemoryRepository{TValue}"/>.
/// Registered as a singleton so multiple interface registrations of the repository
/// all operate on the same data.
/// </summary>
public sealed class InMemoryStore<TValue> : IDisposable, IAsyncDisposable
{
    public sealed class Entry
    {
        public Entry(TValue value, DateTimeOffset? expiresAt)
        {
            Value = value;
            ExpiresAt = expiresAt;
        }

        public TValue Value { get; }
        public DateTimeOffset? ExpiresAt { get; }

        public bool IsExpired(DateTimeOffset now) =>
            ExpiresAt.HasValue && ExpiresAt.Value <= now;
    }

    public ConcurrentDictionary<string, Entry> Data { get; } =
        new(StringComparer.Ordinal);

    private readonly PeriodicTimer? _purgeTimer;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _backgroundTask = Task.CompletedTask;

    public InMemoryStore(InMemoryOptions options)
    {
        if (options.BackgroundPurge)
        {
            _purgeTimer = new PeriodicTimer(options.PurgeInterval);
            _backgroundTask = RunPurgeLoopAsync(_cts.Token);
        }
    }

    public int PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var count = 0;
        foreach (var kvp in Data)
            if (kvp.Value.IsExpired(now) && Data.TryRemove(kvp.Key, out _))
                count++;
        return count;
    }

    private async Task RunPurgeLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _purgeTimer!.WaitForNextTickAsync(ct).ConfigureAwait(false))
                PurgeExpired();
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _purgeTimer?.Dispose();
        _cts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _purgeTimer?.Dispose();
        try { await _backgroundTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _cts.Dispose();
    }
}
