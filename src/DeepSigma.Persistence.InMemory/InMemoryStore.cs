using System.Collections.Concurrent;

namespace DeepSigma.Persistence.InMemory;

/// <summary>
/// Holds the backing store for <see cref="InMemoryRepository{TValue}"/>.
/// Registered as a singleton so multiple interface registrations of the repository
/// all operate on the same data.
/// </summary>
public sealed class InMemoryStore<TValue> : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Represents an entry in the in-memory store, containing the value and its optional expiration time.
    /// </summary>
    public sealed class Entry
    {
        /// <summary>
        ///  Initializes a new instance of the Entry class with the specified value and optional expiration time.
        /// </summary>
        /// <param name="value">The value to associate with this entry.</param>
        /// <param name="expiresAt">The date and time when the entry expires, or null if the entry does not expire.</param>
        public Entry(TValue value, DateTimeOffset? expiresAt)
        {
            Value = value;
            ExpiresAt = expiresAt;
        }

        /// <summary>
        /// Gets the value contained in the current instance.
        /// </summary>
        public TValue Value { get; }

        /// <summary>
        /// Gets the date and time when the entry expires, or null if it does not expire.
        /// </summary>
        public DateTimeOffset? ExpiresAt { get; }

        /// <summary>
        /// Determines whether the current object has expired as of the specified time.
        /// </summary>
        /// <param name="now">The point in time to compare against the expiration date.</param>
        /// <returns>true if the object has an expiration date and it is less than or equal to the specified time; otherwise,
        /// false.</returns>
        public bool IsExpired(DateTimeOffset now) =>
            ExpiresAt.HasValue && ExpiresAt.Value <= now;
    }

    /// <summary>
    /// Gets the collection of entries, indexed by their string keys, that can be accessed and modified concurrently.
    /// </summary>
    /// <remarks>
    /// The dictionary uses case-sensitive, ordinal string comparison for keys. All operations on
    /// this collection are thread-safe.
    /// </remarks>
    public ConcurrentDictionary<string, Entry> Data { get; } = new(StringComparer.Ordinal);

    private readonly PeriodicTimer? _purgeTimer;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _backgroundTask = Task.CompletedTask;

    /// <summary>
    /// Initializes a new instance of the InMemoryStore class with the specified configuration options.
    /// </summary>
    /// <remarks>
    /// If background purging is enabled in the provided options, the store will automatically remove
    /// expired items at the specified interval. This can help manage memory usage in long-running
    /// applications.
    /// </remarks>
    /// <param name="options">
    /// The configuration options that control the behavior of the in-memory store, including background purge settings.
    /// Cannot be null.
    /// </param>
    public InMemoryStore(InMemoryOptions options)
    {
        if (options.BackgroundPurge)
        {
            _purgeTimer = new PeriodicTimer(options.PurgeInterval);
            _backgroundTask = RunPurgeLoopAsync(_cts.Token);
        }
    }

    /// <summary>
    /// Removes all expired items from the collection and returns the number of items removed.
    /// </summary>
    /// <remarks>
    /// This method evaluates each item in the collection for expiration based on the current UTC
    /// time. Only items determined to be expired at the time of the call are removed.
    /// </remarks>
    /// <returns>The number of items that were expired and removed from the collection.</returns>
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

    /// <inheritdoc/>
    public void Dispose()
    {
        _cts.Cancel();
        _purgeTimer?.Dispose();
        _cts.Dispose();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _purgeTimer?.Dispose();
        try { 
            await _backgroundTask.ConfigureAwait(false); 
        }
        catch (OperationCanceledException) { }
        _cts.Dispose();
    }
}
