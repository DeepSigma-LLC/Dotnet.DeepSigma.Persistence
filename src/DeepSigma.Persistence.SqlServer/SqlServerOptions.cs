namespace DeepSigma.Persistence.SqlServer;

/// <summary>
/// Provides configuration options for connecting to and managing persistence in a Microsoft SQL Server database.
/// </summary>
/// <remarks>
/// Supports SQL authentication, Windows-Integrated authentication, and Azure AD authentication via the
/// connection string — see the README "SQL Server" section for examples. SQL Server's clustered-index entry
/// limit caps key length at 450 NVARCHAR characters (≈ 900 bytes); <see cref="PersistenceOptions.MaxKeyLength"/>
/// defaults to 450 here rather than the library-wide 512 for that reason.
/// </remarks>
public sealed class SqlServerOptions : PersistenceOptions
{
    /// <summary>
    /// The connection string used to connect to the SQL Server database. Common shapes:
    /// <list type="bullet">
    /// <item><description><c>Server=localhost;Database=mydb;User Id=app;Password=secret;TrustServerCertificate=true</c> — SQL auth</description></item>
    /// <item><description><c>Server=localhost;Database=mydb;Integrated Security=true;TrustServerCertificate=true</c> — Windows-Integrated auth</description></item>
    /// <item><description><c>Server=myserver.database.windows.net;Database=mydb;Authentication=Active Directory Default</c> — Azure AD</description></item>
    /// </list>
    /// </summary>
    public required string ConnectionString { get; set; }

    /// <summary>
    /// SQL Server schema that contains the persistence table. Defaults to <c>dbo</c>. Identifiers are bracketed
    /// when emitted into SQL so any valid schema name works (including reserved words).
    /// </summary>
    public string Schema { get; set; } = "dbo";

    /// <summary>Gets or sets the name of the database table used for persistence operations.</summary>
    public string TableName { get; set; } = "persistence_kv";

    /// <summary>
    /// Gets or sets a value indicating whether automatic database migrations are enabled.
    /// </summary>
    /// <remarks>When enabled, the system applies pending migrations automatically at startup.
    /// Disabling this property requires manual migration management — see the README "Manual schema migration" section.</remarks>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether background purging of expired entries is enabled.</summary>
    public bool BackgroundPurge { get; set; } = false;

    /// <summary>Gets or sets the interval at which purge operations are performed.</summary>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <inheritdoc/>
    public SqlServerOptions()
    {
        // SQL Server's clustered-index entry max is 900 bytes; NVARCHAR(450) is exactly 900 bytes.
        // Lower than the library-wide 512 default — override at the field level so callers can still raise it
        // (e.g. by using a HASHBYTES-indexed key column in a custom migration).
        MaxKeyLength = 450;
    }

    /// <summary>Validates the current options instance.</summary>
    public override void Validate()
    {
        base.Validate();
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new InvalidOperationException(
                "SqlServerOptions.ConnectionString must be set in the AddSqlServerPersistence configure callback.");
        if (string.IsNullOrWhiteSpace(Schema))
            throw new InvalidOperationException("SqlServerOptions.Schema must not be empty.");
        if (string.IsNullOrWhiteSpace(TableName))
            throw new InvalidOperationException("SqlServerOptions.TableName must not be empty.");
        if (BackgroundPurge && PurgeInterval <= TimeSpan.Zero)
            throw new InvalidOperationException(
                $"SqlServerOptions.PurgeInterval must be positive when BackgroundPurge is enabled (got {PurgeInterval}).");
        if (MaxKeyLength > 450)
            throw new InvalidOperationException(
                $"SqlServerOptions.MaxKeyLength cannot exceed 450 with the default migration " +
                $"(NVARCHAR(450) is the largest SQL Server clustered-index entry; got {MaxKeyLength}).");
    }
}
