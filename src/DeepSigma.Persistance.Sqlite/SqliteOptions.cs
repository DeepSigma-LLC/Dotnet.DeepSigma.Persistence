namespace DeepSigma.Persistance.Sqlite;

public sealed class SqliteOptions : PersistenceOptions
{
    public required string ConnectionString { get; set; }
    public string TableName { get; set; } = "persistence_kv";
    public bool AutoMigrate { get; set; } = true;
    public bool BackgroundPurge { get; set; } = false;
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromMinutes(15);

    public override void Validate()
    {
        base.Validate();
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new InvalidOperationException(
                "SqliteOptions.ConnectionString must be set in the AddSqlitePersistence configure callback.");
        if (string.IsNullOrWhiteSpace(TableName))
            throw new InvalidOperationException("SqliteOptions.TableName must not be empty.");
        if (BackgroundPurge && PurgeInterval <= TimeSpan.Zero)
            throw new InvalidOperationException(
                $"SqliteOptions.PurgeInterval must be positive when BackgroundPurge is enabled (got {PurgeInterval}).");
    }
}
