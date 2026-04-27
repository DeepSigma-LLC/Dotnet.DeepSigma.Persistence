namespace DeepSigma.Persistence.InMemory;

/// <summary>
/// In-memory persistence options. 
/// This backend is designed for testing and single-process scenarios where data does not need to persist across application restarts. 
/// It is not suitable for production use.
/// </summary>
public sealed class InMemoryOptions : PersistenceOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether background purging is enabled.
    /// </summary>
    /// <remarks>
    /// When enabled, background purging automatically removes expired or obsolete items at regular
    /// intervals. Disabling this property may require manual cleanup to prevent resource buildup.
    /// </remarks>
    public bool BackgroundPurge { get; set; } = false;

    /// <summary>
    /// Gets or sets the interval at which purge operations are performed.
    /// </summary>
    /// <remarks>
    /// Adjust this value to control how frequently expired or obsolete data is removed. Setting a
    /// shorter interval may increase resource usage, while a longer interval may delay cleanup.
    /// </remarks>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Validates the current configuration and throws an exception if the settings are invalid.
    /// </summary>
    /// <remarks>
    /// Call this method to ensure that all required options are set to valid values before using the
    /// configuration. This method should be called after setting all relevant properties.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown if background purging is enabled and the purge interval is not a positive time span.</exception>
    public override void Validate()
    {
        base.Validate();
        if (BackgroundPurge && PurgeInterval <= TimeSpan.Zero)
            throw new InvalidOperationException(
                $"InMemoryOptions.PurgeInterval must be positive when BackgroundPurge is enabled (got {PurgeInterval}).");
    }
}
