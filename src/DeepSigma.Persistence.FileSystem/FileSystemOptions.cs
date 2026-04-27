namespace DeepSigma.Persistence.FileSystem;

/// <summary>
/// Single-process only. Not designed for concurrent access across multiple processes.
/// </summary>
public sealed class FileSystemOptions : PersistenceOptions
{
    /// <summary>
    /// The root directory where all data will be stored. 
    /// The library will create subdirectories and files under this path as needed. 
    /// This path must be set to a valid directory path that the application has permission to read and write to. 
    /// It can be an absolute path or a relative path (relative to the application's current working directory). 
    /// If the directory does not exist, the library will attempt to create it when it is first used. If the directory cannot be created or accessed, an exception will be thrown at runtime when the persistence layer is initialized or first accessed.
    /// </summary>
    public required string RootPath { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether background purging is enabled.
    /// </summary>
    /// <remarks>
    /// When enabled, background purging automatically removes expired or obsolete data at regular
    /// intervals without requiring manual intervention. Disabling this property may require explicit cleanup operations
    /// to manage resource usage.
    /// </remarks>
    public bool BackgroundPurge { get; set; } = false;

    /// <summary>
    /// Gets or sets the interval at which purge operations are performed.
    /// </summary>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Validates the current options instance. 
    /// This method checks that the RootPath property is set to a non-empty value and that the PurgeInterval is positive if BackgroundPurge is enabled. 
    /// If any validation checks fail, an InvalidOperationException is thrown with a descriptive error message indicating the specific issue with the configuration. 
    /// This method should be called before using the options to ensure that they are properly configured and to prevent runtime errors due to invalid settings.
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public override void Validate()
    {
        base.Validate();
        if (string.IsNullOrWhiteSpace(RootPath))
            throw new InvalidOperationException(
                "FileSystemOptions.RootPath must be set in the AddFileSystemPersistence configure callback.");
        if (BackgroundPurge && PurgeInterval <= TimeSpan.Zero)
            throw new InvalidOperationException(
                $"FileSystemOptions.PurgeInterval must be positive when BackgroundPurge is enabled (got {PurgeInterval}).");
    }
}
