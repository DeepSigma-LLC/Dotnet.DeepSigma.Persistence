namespace DeepSigma.Persistance;

public interface IRepository<TValue>
{
    /// <summary>Returns the value, or <c>null</c> if the key does not exist or has expired.</summary>
    Task<TValue?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Stores or replaces the value under <paramref name="key"/>. Optional expiry via <paramref name="options"/>.</summary>
    Task SetAsync(string key, TValue value, SetOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Removes the key if it currently exists and has not expired.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the key was visible (existed and not expired) at the time of the call.
    /// <c>false</c> if the key was missing or already expired. Some backends opportunistically
    /// remove expired entries here as a side effect; the boolean return is the contract.
    /// </returns>
    Task<bool> DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>True if the key exists and has not expired.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Enumerates keys, optionally filtered by prefix.
    /// </summary>
    /// <remarks>
    /// <b>Ordering is not guaranteed</b> and may differ between backends. SQL backends currently
    /// return rows sorted by key; in-memory and file-system backends do not. Do not rely on order;
    /// sort at the call site if you need it.
    /// </remarks>
    IAsyncEnumerable<string> ListKeysAsync(string? prefix = null, CancellationToken ct = default);

    /// <summary>
    /// Enumerates key-value pairs, optionally filtered by prefix.
    /// </summary>
    /// <remarks>Same ordering caveat as <see cref="ListKeysAsync(string?, CancellationToken)"/>.</remarks>
    IAsyncEnumerable<KeyValuePair<string, TValue>> ListAsync(string? prefix = null, CancellationToken ct = default);
}
