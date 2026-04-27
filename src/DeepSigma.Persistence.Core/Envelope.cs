namespace DeepSigma.Persistence.Core;

public sealed record Envelope<TValue>
{
    /// <summary>Format version. Current = 1. Readers must handle all known versions; writers always emit current.</summary>
    public int V { get; init; } = 1;

    public required string Key { get; init; }
    public required TValue Value { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }

    public bool IsExpired(DateTimeOffset? asOf = null) =>
        ExpiresAt.HasValue && ExpiresAt.Value <= (asOf ?? DateTimeOffset.UtcNow);
}
