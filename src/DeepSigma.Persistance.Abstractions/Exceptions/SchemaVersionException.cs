namespace DeepSigma.Persistance;

public class SchemaVersionException : PersistenceException
{
    public int CurrentVersion { get; }
    public int RequiredVersion { get; }

    public SchemaVersionException(int currentVersion, int requiredVersion)
        : base(
            $"Schema is at version {currentVersion} but version {requiredVersion} is required. " +
            "Set AutoMigrate = true, or run pending migrations manually before starting the application.")
    {
        CurrentVersion = currentVersion;
        RequiredVersion = requiredVersion;
    }

    public SchemaVersionException(string message) : base(message) { }
    public SchemaVersionException(string message, Exception innerException) : base(message, innerException) { }
}
