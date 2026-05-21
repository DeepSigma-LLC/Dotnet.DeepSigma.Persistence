namespace DeepSigma.Persistence;

/// <summary>
/// Thrown when the backing schema is not at the version the backend expects — typically because
/// migrations have not been applied and <c>AutoMigrate</c> is disabled.
/// </summary>
/// <remarks>
/// Backends key migrations by string id rather than int version (see <c>DeepSigma.DataAccess.RelationalDatabase.MigrationRunner</c>).
/// The message lists the pending migration ids when available.
/// </remarks>
public class SchemaVersionException : PersistenceException
{
    /// <summary>Initializes a new instance with the specified <paramref name="message"/>.</summary>
    public SchemaVersionException(string message) : base(message) { }

    /// <summary>Initializes a new instance with the specified <paramref name="message"/> and <paramref name="innerException"/>.</summary>
    public SchemaVersionException(string message, Exception innerException) : base(message, innerException) { }
}
