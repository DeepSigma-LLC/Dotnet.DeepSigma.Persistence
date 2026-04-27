namespace DeepSigma.Persistence.Sqlite;

/// <summary>
/// Provides configuration options for connecting to and managing persistence in a SQLite database.
/// </summary>
public sealed class SqliteOptions : PersistenceOptions
{
    /// <summary>
    /// The connection string used to connect to the SQLite database.
    /// </summary>
    public required string ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the name of the database table used for key-value persistence.
    /// </summary>
    public string TableName { get; set; } = "persistence_kv";

    /// <summary>
    /// Gets or sets a value indicating whether automatic database migrations are enabled.
    /// </summary>
    /// <remarks>
    /// When set to <see langword="true"/>, the system will attempt to apply pending migrations
    /// automatically at startup. Set to <see langword="false"/> to require manual migration management.
    /// </remarks>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether background purging is enabled.
    /// </summary>
    public bool BackgroundPurge { get; set; } = false;

    /// <summary>
    /// Gets or sets the interval at which purge operations are performed.
    /// </summary>
    /// <remarks>
    /// Adjust this value to control how frequently expired or obsolete data is removed. Setting a
    /// shorter interval may increase resource usage, while a longer interval may delay cleanup.
    /// </remarks>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Validates the current options instance.
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
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
