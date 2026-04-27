using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DeepSigma.Persistence.Core;

namespace DeepSigma.Persistence.InMemory;

public sealed class InMemoryRepository<TValue> : IExpiringRepository<TValue>
{
    private readonly InMemoryStore<TValue> _store;
    private readonly InMemoryOptions _options;

    private ConcurrentDictionary<string, InMemoryStore<TValue>.Entry> Data => _store.Data;

    /// <summary>Convenience constructor for testing and direct instantiation.</summary>
    public InMemoryRepository(InMemoryOptions? options = null)
    {
        _options = options ?? new InMemoryOptions();
        _store = new InMemoryStore<TValue>(_options);
    }

    /// <summary>DI constructor — store is injected as a singleton so all interface registrations share data.</summary>
    public InMemoryRepository(InMemoryStore<TValue> store, InMemoryOptions options)
    {
        _store = store;
        _options = options;
    }

    private void ValidateKey(string key) => KeyValidator.Validate(key, _options.MaxKeyLength);

    // ── IRepository<TValue> ──────────────────────────────────────────────

    public Task<TValue?> GetAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var now = DateTimeOffset.UtcNow;
        if (Data.TryGetValue(key, out var entry) && !entry.IsExpired(now))
            return Task.FromResult<TValue?>(entry.Value);
        return Task.FromResult<TValue?>(default);
    }

    public Task SetAsync(string key, TValue value, SetOptions? options = null, CancellationToken ct = default)
    {
        ValidateKey(key);
        Data[key] = new InMemoryStore<TValue>.Entry(value, options?.ComputeExpiry());
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var now = DateTimeOffset.UtcNow;
        if (!Data.TryRemove(key, out var removed))
            return Task.FromResult(false);
        // Always removes from store (lazy cleanup); only reports true if the entry was visible.
        return Task.FromResult(!removed.IsExpired(now));
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(Data.TryGetValue(key, out var entry) && !entry.IsExpired(now));
    }

#pragma warning disable CS1998 // Synchronous iteration presented as IAsyncEnumerable for interface compliance
    public async IAsyncEnumerable<string> ListKeysAsync(
        string? prefix = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in Data)
        {
            ct.ThrowIfCancellationRequested();
            if (!kvp.Value.IsExpired(now) &&
                (prefix is null || kvp.Key.StartsWith(prefix, StringComparison.Ordinal)))
                yield return kvp.Key;
        }
    }

    public async IAsyncEnumerable<KeyValuePair<string, TValue>> ListAsync(
        string? prefix = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in Data)
        {
            ct.ThrowIfCancellationRequested();
            if (!kvp.Value.IsExpired(now) &&
                (prefix is null || kvp.Key.StartsWith(prefix, StringComparison.Ordinal)))
                yield return new KeyValuePair<string, TValue>(kvp.Key, kvp.Value.Value);
        }
    }
#pragma warning restore CS1998

    // ── IBulkRepository<TValue> ──────────────────────────────────────────

    public Task<IReadOnlyDictionary<string, TValue>> GetManyAsync(
        IEnumerable<string> keys, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var result = new Dictionary<string, TValue>();
        foreach (var key in keys)
        {
            ValidateKey(key);
            if (Data.TryGetValue(key, out var entry) && !entry.IsExpired(now))
                result[key] = entry.Value;
        }
        return Task.FromResult<IReadOnlyDictionary<string, TValue>>(result);
    }

    public Task SetManyAsync(
        IEnumerable<KeyValuePair<string, TValue>> items,
        SetOptions? options = null,
        CancellationToken ct = default)
    {
        var expiry = options?.ComputeExpiry();
        foreach (var (key, value) in items)
        {
            ValidateKey(key);
            Data[key] = new InMemoryStore<TValue>.Entry(value, expiry);
        }
        return Task.CompletedTask;
    }

    public Task<int> DeleteManyAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var count = 0;
        foreach (var key in keys)
        {
            ValidateKey(key);
            if (Data.TryGetValue(key, out var entry) && !entry.IsExpired(now)
                && Data.TryRemove(key, out _))
                count++;
        }
        return Task.FromResult(count);
    }

    // ── IExpiringRepository<TValue> ──────────────────────────────────────

    public Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var now = DateTimeOffset.UtcNow;
        if (!Data.TryGetValue(key, out var entry) || entry.IsExpired(now))
            return Task.FromResult<TimeSpan?>(null);
        return Task.FromResult(entry.ExpiresAt.HasValue
            ? (TimeSpan?)(entry.ExpiresAt.Value - now)
            : null);
    }

    public Task<bool> SetTtlAsync(string key, TimeSpan? ttl, CancellationToken ct = default)
    {
        ValidateKey(key);
        var now = DateTimeOffset.UtcNow;
        var newExpiry = ttl.HasValue ? now + ttl.Value : (DateTimeOffset?)null;

        while (true)
        {
            if (!Data.TryGetValue(key, out var current) || current.IsExpired(now))
                return Task.FromResult(false);
            var updated = new InMemoryStore<TValue>.Entry(current.Value, newExpiry);
            if (Data.TryUpdate(key, updated, current))
                return Task.FromResult(true);
            now = DateTimeOffset.UtcNow;
        }
    }

    public Task<int> PurgeExpiredAsync(CancellationToken ct = default)
        => Task.FromResult(_store.PurgeExpired());
}
