namespace DeepSigma.Persistence;

public sealed record SetOptions
{
    private TimeSpan? _ttl;
    private DateTimeOffset? _absoluteExpiration;

    public TimeSpan? Ttl
    {
        get => _ttl;
        init
        {
            _ttl = value;
            ThrowIfBothSet();
        }
    }

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
