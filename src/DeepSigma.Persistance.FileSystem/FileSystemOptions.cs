namespace DeepSigma.Persistence.FileSystem;

/// <summary>Single-process only. Not designed for concurrent access across multiple processes.</summary>
public sealed class FileSystemOptions : PersistenceOptions
{
    public required string RootPath { get; set; }
    public bool BackgroundPurge { get; set; } = false;
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromMinutes(15);

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
