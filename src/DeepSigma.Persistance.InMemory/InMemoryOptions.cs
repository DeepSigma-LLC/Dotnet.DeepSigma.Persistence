namespace DeepSigma.Persistance.InMemory;

public sealed class InMemoryOptions : PersistenceOptions
{
    public bool BackgroundPurge { get; set; } = false;
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromMinutes(5);

    public override void Validate()
    {
        base.Validate();
        if (BackgroundPurge && PurgeInterval <= TimeSpan.Zero)
            throw new InvalidOperationException(
                $"InMemoryOptions.PurgeInterval must be positive when BackgroundPurge is enabled (got {PurgeInterval}).");
    }
}
