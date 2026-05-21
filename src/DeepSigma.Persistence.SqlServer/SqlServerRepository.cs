using System.Runtime.CompilerServices;
using DeepSigma.DataAccess.RelationalDatabase;
using DeepSigma.DataAccess.SqlServer;
using DeepSigma.Persistence.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepSigma.Persistence.SqlServer;

/// <summary>
/// Microsoft SQL Server repository implementation. Designed for production use; supports concurrent access
/// across multiple processes via SQL Server's row-level locking. Uses a single table to store key/value pairs
/// with optional expiration, mirroring the Postgres backend's surface and contract.
/// </summary>
/// <typeparam name="TValue">The type of values stored in the repository.</typeparam>
public sealed class SqlServerRepository<TValue> : IExpiringRepository<TValue>
{
    private readonly SqlServerOptions _options;
    private readonly IJsonValueSerializer _serializer;
    private readonly RelationalDatabaseApi _db;
    private readonly string _t;  // "[schema].[table]" — pre-built for hot-path SQL formatting

    /// <summary>
    /// Initializes a new instance using DI-supplied dependencies. Runs schema setup before returning.
    /// </summary>
    public SqlServerRepository(
        RelationalDatabaseApi db,
        MigrationRunner migrationRunner,
        SqlServerOptions options,
        IJsonValueSerializer serializer)
    {
        _db = db;
        _options = options;
        _serializer = serializer;
        _t = $"[{options.Schema}].[{options.TableName}]";
        SqlServerSchemaBootstrap.EnsureSchema(migrationRunner, db, options);
    }

    /// <summary>
    /// Convenience constructor for direct construction (e.g. tests). Builds a private connection factory,
    /// <see cref="RelationalDatabaseApi"/>, and <see cref="MigrationRunner"/> from <paramref name="options"/>
    /// and uses the default JSON serializer.
    /// </summary>
    public SqlServerRepository(SqlServerOptions options) : this(BuildDb(options), options, new JsonValueSerializer()) { }

    private SqlServerRepository(RelationalDatabaseApi db, SqlServerOptions options, IJsonValueSerializer serializer)
        : this(
            db,
            new MigrationRunner(db, SqlServerSchemaBootstrap.CreateMigrationsTableSql, NullLogger<MigrationRunner>.Instance),
            options,
            serializer)
    { }

    private static RelationalDatabaseApi BuildDb(SqlServerOptions options)
    {
        var factory = new SqlServerConnectionFactory(options.ConnectionString);
        return new RelationalDatabaseApi(factory, NullLogger<RelationalDatabaseApi>.Instance);
    }

    private void ValidateKey(string key) => KeyValidator.Validate(key, _options.MaxKeyLength);

    private static string LiveWhere =>
        "([expires_at] IS NULL OR [expires_at] > SYSDATETIMEOFFSET())";

    // ── IRepository<TValue> ──────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<TValue?> GetAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var val = await _db.QueryFirstOrDefaultAsync<object, string>(
            $"SELECT [value] FROM {_t} WHERE [key] = @Key AND {LiveWhere}",
            new { Key = key }, cancellationToken: ct).ConfigureAwait(false);
        return val is null ? default : _serializer.DeserializeFromString<TValue>(val);
    }

    /// <inheritdoc/>
    public async Task SetAsync(string key, TValue value, SetOptions? options = null, CancellationToken ct = default)
    {
        ValidateKey(key);
        await _db.ExecuteAsync(UpsertSql, new
        {
            Key = key,
            Value = _serializer.SerializeToString(value),
            Now = DateTimeOffset.UtcNow,
            ExpiresAt = options?.ComputeExpiry()
        }, cancellationToken: ct).ConfigureAwait(false);
    }

    // UPDLOCK + HOLDLOCK on the UPDATE acquires a key-range lock covering the missing row, so a second
    // concurrent caller blocks on the UPDATE rather than racing into the INSERT and hitting a PK violation.
    // SET XACT_ABORT ON ensures any unexpected error inside the transaction is rolled back atomically.
    // This is the documented safe upsert pattern for SQL Server (preferred over MERGE for two-row upserts).
    private string UpsertSql => $"""
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;

        UPDATE {_t} WITH (UPDLOCK, HOLDLOCK)
        SET [value] = @Value,
            [updated_at] = @Now,
            [expires_at] = @ExpiresAt
        WHERE [key] = @Key;

        IF @@ROWCOUNT = 0
            INSERT INTO {_t} ([key], [value], [created_at], [updated_at], [expires_at])
            VALUES (@Key, @Value, @Now, @Now, @ExpiresAt);

        COMMIT TRANSACTION;
        """;

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var rows = await _db.ExecuteAsync(
            $"DELETE FROM {_t} WHERE [key] = @Key AND {LiveWhere}",
            new { Key = key }, cancellationToken: ct).ConfigureAwait(false);
        return rows > 0;
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var count = await _db.ExecuteScalarAsync<object, int>(
            $"SELECT COUNT(1) FROM {_t} WHERE [key] = @Key AND {LiveWhere}",
            new { Key = key }, cancellationToken: ct).ConfigureAwait(false);
        return count > 0;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ListKeysAsync(
        string? prefix = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (where, prefixValue) = SqlPrefix.Build(prefix);
        var sql = $"SELECT [key] FROM {_t} WHERE {LiveWhere}{where} ORDER BY [key]";
        var stream = prefixValue is null
            ? _db.QueryStreamAsync<string>(sql, cancellationToken: ct)
            : _db.QueryStreamAsync<object, string>(sql, new { Prefix = prefixValue }, cancellationToken: ct);
        await foreach (var key in stream.WithCancellation(ct).ConfigureAwait(false))
            yield return key;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<KeyValuePair<string, TValue>> ListAsync(
        string? prefix = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (where, prefixValue) = SqlPrefix.Build(prefix);
        var sql = $"SELECT [key] AS [Key], [value] AS [Value] FROM {_t} WHERE {LiveWhere}{where} ORDER BY [key]";
        var stream = prefixValue is null
            ? _db.QueryStreamAsync<KeyValueRow>(sql, cancellationToken: ct)
            : _db.QueryStreamAsync<object, KeyValueRow>(sql, new { Prefix = prefixValue }, cancellationToken: ct);
        await foreach (var row in stream.WithCancellation(ct).ConfigureAwait(false))
            yield return new KeyValuePair<string, TValue>(row.Key, _serializer.DeserializeFromString<TValue>(row.Value)!);
    }

    private sealed record KeyValueRow(string Key, string Value);

    // ── IBulkRepository<TValue> ──────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, TValue>> GetManyAsync(
        IEnumerable<string> keys, CancellationToken ct = default)
    {
        var result = new Dictionary<string, TValue>();
        foreach (var chunk in KeyValidator.ValidateAll(keys, _options.MaxKeyLength).Chunk(SqlBulkChunk.Size))
        {
            // Dapper expands "IN @Keys" into "IN (@p0, @p1, ...)" at execution time. With our chunk size
            // of 1 000 this stays well under SQL Server's 2 100-parameter limit.
            var rows = await _db.GetAllAsync<object, KeyValueRow>(
                $"SELECT [key] AS [Key], [value] AS [Value] FROM {_t} WHERE [key] IN @Keys AND {LiveWhere}",
                new { Keys = chunk }, cancellationToken: ct).ConfigureAwait(false);
            foreach (var row in rows)
                result[row.Key] = _serializer.DeserializeFromString<TValue>(row.Value)!;
        }
        return result;
    }

    /// <inheritdoc/>
    public async Task SetManyAsync(
        IEnumerable<KeyValuePair<string, TValue>> items,
        SetOptions? options = null,
        CancellationToken ct = default)
    {
        var expiry = options?.ComputeExpiry();
        var now = DateTimeOffset.UtcNow;
        foreach (var chunk in items.Chunk(SqlBulkChunk.Size))
        {
            var rows = chunk.Select(kv =>
            {
                ValidateKey(kv.Key);
                return new
                {
                    Key = kv.Key,
                    Value = _serializer.SerializeToString(kv.Value),
                    Now = now,
                    ExpiresAt = expiry
                };
            });
            // The upsert SQL itself wraps each row in BEGIN/COMMIT TRAN; nesting inside the outer
            // BeginTransactionAsync simply increments @@TRANCOUNT — only the outer commit actually commits.
            await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct).ConfigureAwait(false);
            await tx.ExecuteAllAsync(UpsertSql, rows, cancellationToken: ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<int> DeleteManyAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        var total = 0;
        foreach (var chunk in KeyValidator.ValidateAll(keys, _options.MaxKeyLength).Chunk(SqlBulkChunk.Size))
        {
            total += await _db.ExecuteAsync(
                $"DELETE FROM {_t} WHERE [key] IN @Keys AND {LiveWhere}",
                new { Keys = chunk }, cancellationToken: ct).ConfigureAwait(false);
        }
        return total;
    }

    // ── IExpiringRepository<TValue> ──────────────────────────────────────

    private sealed record ExpiresAtRow(DateTimeOffset? ExpiresAt);

    /// <inheritdoc/>
    public async Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        // null row = key missing or expired; ExpiresAt = null = key lives forever
        var row = await _db.QueryFirstOrDefaultAsync<object, ExpiresAtRow>(
            $"SELECT [expires_at] AS [ExpiresAt] FROM {_t} WHERE [key] = @Key AND {LiveWhere}",
            new { Key = key }, cancellationToken: ct).ConfigureAwait(false);
        if (row is null) return null;
        if (row.ExpiresAt is null) return null;
        return row.ExpiresAt.Value - DateTimeOffset.UtcNow;
    }

    /// <inheritdoc/>
    public async Task<bool> SetTtlAsync(string key, TimeSpan? ttl, CancellationToken ct = default)
    {
        ValidateKey(key);
        var rows = await _db.ExecuteAsync(
            $"UPDATE {_t} SET [expires_at] = @ExpiresAt, [updated_at] = @Now WHERE [key] = @Key AND {LiveWhere}",
            new
            {
                Key = key,
                ExpiresAt = ttl.HasValue ? (DateTimeOffset?)(DateTimeOffset.UtcNow + ttl.Value) : null,
                Now = DateTimeOffset.UtcNow
            }, cancellationToken: ct).ConfigureAwait(false);
        return rows > 0;
    }

    /// <inheritdoc/>
    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        return await _db.ExecuteAsync(
            $"DELETE FROM {_t} WHERE [expires_at] IS NOT NULL AND [expires_at] <= SYSDATETIMEOFFSET()",
            cancellationToken: ct).ConfigureAwait(false);
    }
}
