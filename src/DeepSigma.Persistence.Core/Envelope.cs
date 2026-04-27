namespace DeepSigma.Persistence.Core;

/// <summary>
/// A simple envelope for values stored in the persistence layer. This is the "payload" that gets serialized and deserialized by <c>IJsonValueSerializer</c>.
/// </summary>
/// <typeparam name="TValue">The type of the value being stored in the envelope.</typeparam>
public sealed record Envelope<TValue>
{
    /// <summary>
    /// Format version. Current = 1. Readers must handle all known versions; writers always emit current.
    /// </summary>
    public int V { get; init; } = 1;

    /// <summary>
    /// The key associated with the value. This is not necessarily the same as the "key" used by the persistence layer to look up the record; it's just a string that gets stored alongside the value for informational/debugging purposes. 
    /// The persistence layer may choose to ignore this field entirely, or it may choose to populate it with the actual key used for lookup, but that behavior is not defined by this library and should not be relied upon.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the value associated with this instance.
    /// </summary>
    public required TValue Value { get; init; }

    /// <summary>
    /// Gets the date and time when the entity was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets the date and time when the entity was last updated.
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Gets the date and time when the entity expires, or null if it does not expire.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Determines whether the current object is expired as of the specified date and time.
    /// </summary>
    /// <param name="asOf">The point in time to evaluate expiration against. If null, the current UTC time is used.</param>
    /// <returns>true if the object is expired as of the specified time; otherwise, false.</returns>
    public bool IsExpired(DateTimeOffset? asOf = null) =>
        ExpiresAt.HasValue && ExpiresAt.Value <= (asOf ?? DateTimeOffset.UtcNow);
}
