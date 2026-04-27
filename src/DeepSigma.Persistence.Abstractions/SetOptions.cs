namespace DeepSigma.Persistence;

/// <summary>Controls expiration behaviour when writing a value. Set at most one of <see cref="Ttl"/> or <see cref="AbsoluteExpiration"/>.</summary>
public sealed record SetOptions
{
    private TimeSpan? _ttl;
    private DateTimeOffset? _absoluteExpiration;

    /// <summary>Time-to-live relative to the moment of the write. Mutually exclusive with <see cref="AbsoluteExpiration"/>.</summary>
    public TimeSpan? Ttl
    {
        get => _ttl;
        init
        {
            _ttl = value;
            ThrowIfBothSet();
        }
    }

    /// <summary>Fixed point-in-time after which the entry expires. Mutually exclusive with <see cref="Ttl"/>.</summary>
    public DateTimeOffset? AbsoluteExpiration
    {
        get => _absoluteExpiration;
        init
        {
            _absoluteExpiration = value;
            ThrowIfBothSet();
        }
    }

    private void ThrowIfBothSet()
    {
        if (_ttl.HasValue && _absoluteExpiration.HasValue)
            throw new ArgumentException("Ttl and AbsoluteExpiration are mutually exclusive; set only one.");
    }

    /// <summary>
    /// Resolves these options into an absolute expiration timestamp, or null if no expiry is configured.
    /// Backends call this to translate the user-facing TTL/AbsoluteExpiration choice into a single value.
    /// </summary>
    public DateTimeOffset? ComputeExpiry(DateTimeOffset? now = null) => this switch
    {
        { Ttl: { } ttl } => (now ?? DateTimeOffset.UtcNow) + ttl,
        { AbsoluteExpiration: { } abs } => abs,
        _ => null
    };
}
