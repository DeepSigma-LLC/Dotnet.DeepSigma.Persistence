namespace DeepSigma.Persistence.Postgres;

/// <summary>
/// Provides configuration options for connecting to and managing persistence in a PostgreSQL database.
/// </summary>
/// <remarks>
/// Use this class to specify connection details, table naming, migration behavior, and background data
/// purging for PostgreSQL-based persistence. These options are typically configured during application startup when
/// registering persistence services.
/// </remarks>
public sealed class PostgresOptions : PersistenceOptions
{
    /// <summary>
    /// The connection string used to connect to the PostgreSQL database. 
    /// This string must be set to a valid PostgreSQL connection string that includes the necessary parameters such as server address, database name, user credentials, and any other required options for establishing a connection. 
    /// The library will use this connection string to create a data source for executing queries and managing persistence operations. 
    /// If this property is not set or is invalid, an exception will be thrown at runtime when the persistence layer is initialized or first accessed.
    /// </summary>
    public required string ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the name of the database table used for persistence operations.
    /// </summary>
    public string TableName { get; set; } = "persistence_kv";

    /// <summary>
    /// Gets or sets a value indicating whether automatic database migrations are enabled.
    /// </summary>
    /// <remarks>When enabled, the system will attempt to apply pending migrations automatically at startup.
    /// Disabling this property requires manual migration management.</remarks>
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
                "PostgresOptions.ConnectionString must be set in the AddPostgresPersistence configure callback.");
        if (string.IsNullOrWhiteSpace(TableName))
            throw new InvalidOperationException("PostgresOptions.TableName must not be empty.");
        if (BackgroundPurge && PurgeInterval <= TimeSpan.Zero)
            throw new InvalidOperationException(
                $"PostgresOptions.PurgeInterval must be positive when BackgroundPurge is enabled (got {PurgeInterval}).");
    }
}
