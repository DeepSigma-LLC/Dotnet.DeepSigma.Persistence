namespace DeepSigma.Persistence;

/// <summary>Thrown when the database schema version does not meet the minimum required version.</summary>
public class SchemaVersionException : PersistenceException
{
    /// <summary>The schema version currently present in the database.</summary>
    public int CurrentVersion { get; }
    /// <summary>The minimum schema version required by the backend.</summary>
    public int RequiredVersion { get; }

    /// <summary>Initializes a new instance with the detected <paramref name="currentVersion"/> and <paramref name="requiredVersion"/>.</summary>
    public SchemaVersionException(int currentVersion, int requiredVersion)
        : base(
            $"Schema is at version {currentVersion} but version {requiredVersion} is required. " +
            "Set AutoMigrate = true, or run pending migrations manually before starting the application.")
    {
        CurrentVersion = currentVersion;
        RequiredVersion = requiredVersion;
    }

    /// <summary>Initializes a new instance with the specified <paramref name="message"/>.</summary>
    public SchemaVersionException(string message) : base(message) { }
    /// <summary>Initializes a new instance with the specified <paramref name="message"/> and <paramref name="innerException"/>.</summary>
    public SchemaVersionException(string message, Exception innerException) : base(message, innerException) { }
}
