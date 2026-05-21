using System.Globalization;
using System.Runtime.CompilerServices;
using DeepSigma.DataAccess.RelationalDatabase;
using DeepSigma.DataAccess.Sqlite;
using DeepSigma.Persistence.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepSigma.Persistence.Sqlite;

/// <summary>
/// SQLite repository implementation. This backend is suitable for single-process scenarios where a lightweight, file-based persistence solution is desired. It supports expiring entries and is designed to be used in applications that do not require concurrent access across multiple processes. For multi-process scenarios, consider using a client-server database like PostgreSQL.
/// </summary>
/// <typeparam name="TValue">The type of values stored in the repository.</typeparam>
public sealed class SqliteRepository<TValue> : IExpiringRepository<TValue>
{
    private readonly SqliteOptions _options;
    private readonly IJsonValueSerializer _serializer;
    private readonly RelationalDatabaseApi _db;
    private readonly string _t; // table name shorthand

    /// <summary>
    /// Initializes a new instance using DI-supplied dependencies. Runs schema setup before returning.
    /// </summary>
    /// <param name="db">The Dapper-backed database API supplied by the SQLite provider.</param>
    /// <param name="migrationRunner">The migration runner used to apply pending migrations at startup.</param>
    /// <param name="options">The configuration options for the SQLite repository.</param>
    /// <param name="serializer">The JSON serializer used for serializing and deserializing values.</param>
    public SqliteRepository(
        RelationalDatabaseApi db,
        MigrationRunner migrationRunner,
        SqliteOptions options,
        IJsonValueSerializer serializer)
    {
        _db = db;
        _options = options;
        _serializer = serializer;
        _t = options.TableName;
        SqliteSchemaBootstrap.EnsureSchema(migrationRunner, db, options);
    }

    /// <summary>
    /// Convenience constructor for direct construction (e.g. tests). Builds a private connection factory,
    /// <see cref="RelationalDatabaseApi"/>, and <see cref="MigrationRunner"/> from <paramref name="options"/>
    /// and uses the default JSON serializer.
    /// </summary>
    public SqliteRepository(SqliteOptions options) : this(BuildDb(options), options, new JsonValueSerializer()) { }

    private SqliteRepository(RelationalDatabaseApi db, SqliteOptions options, IJsonValueSerializer serializer)
        : this(
            db,
            new MigrationRunner(db, SqliteSchemaBootstrap.CreateMigrationsTableSql, NullLogger<MigrationRunner>.Instance),
            options,
            serializer)
    { }

    private static RelationalDatabaseApi BuildDb(SqliteOptions options)
    {
        var factory = new SqliteConnectionFactory(options.ConnectionString, SqliteSchemaBootstrap.ApplyConnectionPragmas);
        return new RelationalDatabaseApi(factory, NullLogger<RelationalDatabaseApi>.Instance);
    }

    private void ValidateKey(string key) => KeyValidator.Validate(key, _options.MaxKeyLength);

    // Millisecond precision so short TTLs compare correctly against strftime('%Y-%m-%d %H:%M:%f','now').
    private static string ToSqlite(DateTimeOffset dt) =>
        dt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff");

    private static string? ToSqlite(DateTimeOffset? dt) =>
        dt.HasValue ? ToSqlite(dt.Value) : null;

    // Use strftime with %f for subsecond precision — matches the .fff stored format.
    private static string LiveWhere =>
        "(expires_at IS NULL OR expires_at > strftime('%Y-%m-%d %H:%M:%f','now'))";

    // ── IRepository<TValue> ──────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<TValue?> GetAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var val = await _db.QueryFirstOrDefaultAsync<object, string>(
            $"SELECT value FROM {_t} WHERE key = @Key AND {LiveWhere}",
            new { Key = key }, cancellationToken: ct).ConfigureAwait(false);
        return val is null ? default : _serializer.DeserializeFromString<TValue>(val);
    }

    /// <inheritdoc/>
    public async Task SetAsync(string key, TValue value, SetOptions? options = null, CancellationToken ct = default)
    {
        ValidateKey(key);
        var now = ToSqlite(DateTimeOffset.UtcNow);
        await _db.ExecuteAsync(
            $"""
             INSERT INTO {_t}(key, value, created_at, updated_at, expires_at)
             VALUES(@Key, @Value, @Now, @Now, @ExpiresAt)
             ON CONFLICT(key) DO UPDATE SET
                 value      = excluded.value,
                 updated_at = excluded.updated_at,
                 expires_at = excluded.expires_at
             """,
            new
            {
                Key = key,
                Value = _serializer.SerializeToString(value),
                Now = now,
                ExpiresAt = ToSqlite(options?.ComputeExpiry())
            }, cancellationToken: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var rows = await _db.ExecuteAsync(
            $"DELETE FROM {_t} WHERE key = @Key AND {LiveWhere}",
            new { Key = key }, cancellationToken: ct).ConfigureAwait(false);
        return rows > 0;
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var count = await _db.ExecuteScalarAsync<object, int>(
            $"SELECT COUNT(1) FROM {_t} WHERE key = @Key AND {LiveWhere}",
            new { Key = key }, cancellationToken: ct).ConfigureAwait(false);
        return count > 0;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ListKeysAsync(
        string? prefix = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (where, prefixValue) = SqlPrefix.Build(prefix);
        var sql = $"SELECT key FROM {_t} WHERE {LiveWhere}{where} ORDER BY key";
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
        var sql = $"SELECT key AS Key, value AS Value FROM {_t} WHERE {LiveWhere}{where} ORDER BY key";
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
            var rows = await _db.GetAllAsync<object, KeyValueRow>(
                $"SELECT key AS Key, value AS Value FROM {_t} WHERE key IN @Keys AND {LiveWhere}",
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
        var expiry = ToSqlite(options?.ComputeExpiry());
        var now = ToSqlite(DateTimeOffset.UtcNow);
        var sql = $"""
            INSERT INTO {_t}(key, value, created_at, updated_at, expires_at)
            VALUES(@Key, @Value, @Now, @Now, @ExpiresAt)
            ON CONFLICT(key) DO UPDATE SET
                value      = excluded.value,
                updated_at = excluded.updated_at,
                expires_at = excluded.expires_at
            """;
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
            await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct).ConfigureAwait(false);
            await tx.ExecuteAllAsync(sql, rows, cancellationToken: ct).ConfigureAwait(false);
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
                $"DELETE FROM {_t} WHERE key IN @Keys AND {LiveWhere}",
                new { Keys = chunk }, cancellationToken: ct).ConfigureAwait(false);
        }
        return total;
    }

    // ── IExpiringRepository<TValue> ──────────────────────────────────────

    // Used by GetTtlAsync. Defined as a class (not a value tuple) so Dapper's column-to-property
    // mapping is robust across versions and so `null` distinguishes "no row" from "no expiry."
    private sealed record ExpiresAtRow(string? ExpiresAt);

    private const string SqliteTimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";

    /// <inheritdoc/>
    public async Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        // null row     => key missing (or expired and filtered by LiveWhere)
        // ExpiresAt=null => key exists with no expiry
        var row = await _db.QueryFirstOrDefaultAsync<object, ExpiresAtRow>(
            $"SELECT expires_at AS ExpiresAt FROM {_t} WHERE key = @Key AND {LiveWhere}",
            new { Key = key }, cancellationToken: ct).ConfigureAwait(false);
        if (row is null) return null;
        if (row.ExpiresAt is null) return null;

        // ParseExact (not Parse) so a future format drift fails loudly instead of being silently
        // misinterpreted. AssumeUniversal because we always store UTC; AdjustToUniversal pins the
        // offset to zero on the returned DateTimeOffset.
        var expiry = DateTimeOffset.ParseExact(
            row.ExpiresAt,
            SqliteTimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        return expiry - DateTimeOffset.UtcNow;
    }

    /// <inheritdoc/>
    public async Task<bool> SetTtlAsync(string key, TimeSpan? ttl, CancellationToken ct = default)
    {
        ValidateKey(key);
        var rows = await _db.ExecuteAsync(
            $"UPDATE {_t} SET expires_at = @ExpiresAt, updated_at = @Now WHERE key = @Key AND {LiveWhere}",
            new
            {
                Key = key,
                ExpiresAt = ttl.HasValue ? ToSqlite(DateTimeOffset.UtcNow + ttl.Value) : null,
                Now = ToSqlite(DateTimeOffset.UtcNow)
            }, cancellationToken: ct).ConfigureAwait(false);
        return rows > 0;
    }

    /// <inheritdoc/>
    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        return await _db.ExecuteAsync(
            $"DELETE FROM {_t} WHERE expires_at IS NOT NULL AND expires_at <= strftime('%Y-%m-%d %H:%M:%f','now')",
            cancellationToken: ct).ConfigureAwait(false);
    }
}
