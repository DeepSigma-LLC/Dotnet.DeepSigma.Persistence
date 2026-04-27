namespace DeepSigma.Persistence;

/// <summary>Extends <see cref="IRepository{TValue}"/> with batch read/write/delete operations.</summary>
public interface IBulkRepository<TValue> : IRepository<TValue>
{
    /// <summary>Returns only found keys — missing keys are absent from the dictionary, never null-valued.</summary>
    Task<IReadOnlyDictionary<string, TValue>> GetManyAsync(IEnumerable<string> keys, CancellationToken ct = default);

    /// <summary>Stores or replaces multiple entries in a single operation. Optional expiry via <paramref name="options"/>.</summary>
    Task SetManyAsync(IEnumerable<KeyValuePair<string, TValue>> items, SetOptions? options = null, CancellationToken ct = default);

    /// <returns>Count of keys actually deleted.</returns>
    Task<int> DeleteManyAsync(IEnumerable<string> keys, CancellationToken ct = default);
}
