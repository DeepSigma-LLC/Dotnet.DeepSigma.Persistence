namespace DeepSigma.Persistance;

public interface IExpiringRepository<TValue> : IBulkRepository<TValue>
{
    /// <returns>Remaining TTL, or null if the key has no expiration (or does not exist).</returns>
    Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Sets or removes expiration on an existing key.
    /// Passing null removes any existing expiration.
    /// </summary>
    /// <returns>False if the key does not exist.</returns>
    Task<bool> SetTtlAsync(string key, TimeSpan? ttl, CancellationToken ct = default);

    /// <returns>Count of expired records removed.</returns>
    Task<int> PurgeExpiredAsync(CancellationToken ct = default);
}
