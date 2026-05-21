using System.Runtime.CompilerServices;
using DeepSigma.DataAccess.Postgres;
using DeepSigma.DataAccess.RelationalDatabase;
using DeepSigma.Persistence.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepSigma.Persistence.Postgres;

/// <summary>
/// PostgreSQL repository implementation. This backend is designed for production use and supports concurrent access across multiple processes.
/// It uses a single table to store key-value pairs, with optional expiration times.
/// The repository ensures that expired entries are not returned in queries, and provides methods for managing TTL and purging expired entries.
/// The JSON serializer is used to convert values to and from their string representation for storage in the database.
/// </summary>
/// <typeparam name="TValue"></typeparam>
public sealed class PostgresRepository<TValue> : IExpiringRepository<TValue>
{
    private readonly PostgresOptions _options;
    private readonly IJsonValueSerializer _serializer;
    private readonly RelationalDatabaseApi _db;
    private readonly string _t;

    /// <summary>
    /// Initializes a new instance using DI-supplied dependencies. Runs schema setup before returning.
    /// </summary>
    /// <param name="db">The Dapper-backed database API supplied by the Postgres provider.</param>
    /// <param name="migrationRunner">The migration runner used to apply pending migrations at startup.</param>
    /// <param name="options">The configuration options for the Postgres repository.</param>
    /// <param name="serializer">The JSON serializer used for serializing and deserializing values.</param>
    public PostgresRepository(
        RelationalDatabaseApi db,
        MigrationRunner migrationRunner,
        PostgresOptions options,
        IJsonValueSerializer serializer)
    {
        _db = db;
        _options = options;
        _serializer = serializer;
        _t = options.TableName;
        PostgresSchemaBootstrap.EnsureSchema(migrationRunner, db, options);
    }

    /// <summary>
    /// Convenience constructor for direct construction (e.g. tests). Builds a private connection factory,
    /// <see cref="RelationalDatabaseApi"/>, and <see cref="MigrationRunner"/> from <paramref name="options"/>
    /// and uses the default JSON serializer.
    /// </summary>
    public PostgresRepository(PostgresOptions options) : this(BuildDb(options), options, new JsonValueSerializer()) { }

    private PostgresRepository(RelationalDatabaseApi db, PostgresOptions options, IJsonValueSerializer serializer)
        : this(
            db,
            new MigrationRunner(db, PostgresSchemaBootstrap.CreateMigrationsTableSql, NullLogger<MigrationRunner>.Instance),
            options,
            serializer)
    { }

    private static RelationalDatabaseApi BuildDb(PostgresOptions options)
    {
        var factory = new PostgresConnectionFactory(options.ConnectionString);
        return new RelationalDatabaseApi(factory, NullLogger<RelationalDatabaseApi>.Instance);
    }

    private void ValidateKey(string key) => KeyValidator.Validate(key, _options.MaxKeyLength);

    private static string LiveWhere =>
        "(expires_at IS NULL OR expires_at > NOW())";

    // ── IRepository<TValue> ──────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<TValue?> GetAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var val = await _db.QueryFirstOrDefaultAsync<object, string>(
            $"SELECT value::text FROM {_t} WHERE key = @Key AND {LiveWhere}",
            new { Key = key }, cancellationToken: ct).ConfigureAwait(false);
        return val is null ? default : _serializer.DeserializeFromString<TValue>(val);
    }

    /// <inheritdoc/>
    public async Task SetAsync(string key, TValue value, SetOptions? options = null, CancellationToken ct = default)
    {
        ValidateKey(key);
        await _db.ExecuteAsync(
            $"""
             INSERT INTO {_t}(key, value, created_at, updated_at, expires_at)
             VALUES(@Key, CAST(@Value AS jsonb), @Now, @Now, @ExpiresAt)
             ON CONFLICT(key) DO UPDATE SET
                 value      = excluded.value,
                 updated_at = excluded.updated_at,
                 expires_at = excluded.expires_at
             """,
            new
            {
                Key = key,
                Value = _serializer.SerializeToString(value),
                Now = DateTimeOffset.UtcNow,
                ExpiresAt = options?.ComputeExpiry()
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
        var sql = $"SELECT key AS Key, value::text AS Value FROM {_t} WHERE {LiveWhere}{where} ORDER BY key";
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
                $"SELECT key AS Key, value::text AS Value FROM {_t} WHERE key = ANY(@Keys) AND {LiveWhere}",
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
        var sql = $"""
            INSERT INTO {_t}(key, value, created_at, updated_at, expires_at)
            VALUES(@Key, CAST(@Value AS jsonb), @Now, @Now, @ExpiresAt)
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
                $"DELETE FROM {_t} WHERE key = ANY(@Keys) AND {LiveWhere}",
                new { Keys = chunk }, cancellationToken: ct).ConfigureAwait(false);
        }
        return total;
    }

    // ── IExpiringRepository<TValue> ──────────────────────────────────────

    private sealed record ExpiresAtRow(DateTime? ExpiresAt);

    /// <inheritdoc/>
    public async Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        // null row = key missing or expired; ExpiresAt = null = key lives forever
        var row = await _db.QueryFirstOrDefaultAsync<object, ExpiresAtRow>(
            $"SELECT expires_at AS \"ExpiresAt\" FROM {_t} WHERE key = @Key AND {LiveWhere}",
            new { Key = key }, cancellationToken: ct).ConfigureAwait(false);
        if (row is null) return null;
        if (row.ExpiresAt is null) return null;
        return new DateTimeOffset(row.ExpiresAt.Value, TimeSpan.Zero) - DateTimeOffset.UtcNow;
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
                ExpiresAt = ttl.HasValue ? (DateTimeOffset?)(DateTimeOffset.UtcNow + ttl.Value) : null,
                Now = DateTimeOffset.UtcNow
            }, cancellationToken: ct).ConfigureAwait(false);
        return rows > 0;
    }

    /// <inheritdoc/>
    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        return await _db.ExecuteAsync(
            $"DELETE FROM {_t} WHERE expires_at IS NOT NULL AND expires_at <= NOW()",
            cancellationToken: ct).ConfigureAwait(false);
    }
}
