using System.Runtime.CompilerServices;
using Dapper;
using DeepSigma.Persistence.Core;
using Npgsql;

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
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _t;

    /// <summary>
    /// Initializes a new instance of the PostgresRepository class using the specified data source, options, and JSON
    /// value serializer.
    /// </summary>
    /// <remarks>This constructor ensures that the required database schema is present by invoking schema
    /// migrations based on the provided options. All parameters must be valid and non-null to ensure correct repository
    /// initialization.</remarks>
    /// <param name="dataSource">The NpgsqlDataSource instance used to manage database connections for PostgreSQL operations. Cannot be null.</param>
    /// <param name="options">The configuration options that define repository behavior, including the target table name and schema settings.
    /// Cannot be null.</param>
    /// <param name="serializer">The serializer used to convert objects to and from JSON for storage and retrieval. Cannot be null.</param>
    public PostgresRepository(NpgsqlDataSource dataSource, PostgresOptions options, IJsonValueSerializer serializer)
    {
        _options = options;
        _serializer = serializer;
        _dataSource = dataSource;
        _t = options.TableName;
        PostgresMigrations.EnsureSchema(options);
    }

    /// <summary>
    /// Convenience constructor for testing; uses <see cref="JsonValueSerializer"/>.
    /// </summary>
    public PostgresRepository(NpgsqlDataSource dataSource, PostgresOptions options)
        : this(dataSource, options, new JsonValueSerializer()) { }

    private void ValidateKey(string key) => KeyValidator.Validate(key, _options.MaxKeyLength);

    private static string LiveWhere =>
        "(expires_at IS NULL OR expires_at > NOW())";

    // ── IRepository<TValue> ──────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<TValue?> GetAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var val = await conn.QueryFirstOrDefaultAsync<string>(
            $"SELECT value::text FROM {_t} WHERE key = @Key AND {LiveWhere}",
            new { Key = key }).ConfigureAwait(false);
        return val is null ? default : _serializer.DeserializeFromString<TValue>(val);
    }

    /// <inheritdoc/>
    public async Task SetAsync(string key, TValue value, SetOptions? options = null, CancellationToken ct = default)
    {
        ValidateKey(key);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
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
            }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var rows = await conn.ExecuteAsync(
            $"DELETE FROM {_t} WHERE key = @Key AND {LiveWhere}",
            new { Key = key }).ConfigureAwait(false);
        return rows > 0;
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var count = await conn.QueryFirstAsync<int>(
            $"SELECT COUNT(1) FROM {_t} WHERE key = @Key AND {LiveWhere}",
            new { Key = key }).ConfigureAwait(false);
        return count > 0;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ListKeysAsync(
        string? prefix = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (where, prefixValue) = SqlPrefix.Build(prefix);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = $"SELECT key FROM {_t} WHERE {LiveWhere}{where} ORDER BY key";
        if (prefixValue is not null)
            cmd.Parameters.AddWithValue("Prefix", prefixValue);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            yield return reader.GetString(0);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<KeyValuePair<string, TValue>> ListAsync(
        string? prefix = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (where, prefixValue) = SqlPrefix.Build(prefix);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT key, value::text FROM {_t} WHERE {LiveWhere}{where} ORDER BY key";
        if (prefixValue is not null)
            cmd.Parameters.AddWithValue("Prefix", prefixValue);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            yield return new KeyValuePair<string, TValue>(reader.GetString(0), _serializer.DeserializeFromString<TValue>(reader.GetString(1))!);
    }

    // ── IBulkRepository<TValue> ──────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, TValue>> GetManyAsync(
        IEnumerable<string> keys, CancellationToken ct = default)
    {
        var result = new Dictionary<string, TValue>();
        foreach (var chunk in KeyValidator.ValidateAll(keys, _options.MaxKeyLength).Chunk(1000))
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            var rows = await conn.QueryAsync<(string Key, string Value)>(
                $"SELECT key, value::text FROM {_t} WHERE key = ANY(@Keys) AND {LiveWhere}",
                new { Keys = chunk }).ConfigureAwait(false);
            foreach (var (key, value) in rows)
                result[key] = _serializer.DeserializeFromString<TValue>(value)!;
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
        foreach (var chunk in items.Chunk(1000))
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            foreach (var (key, value) in chunk)
            {
                ValidateKey(key);
                await conn.ExecuteAsync(sql,
                    new { Key = key, Value = _serializer.SerializeToString(value), Now = now, ExpiresAt = expiry }, tx)
                    .ConfigureAwait(false);
            }
            await tx.CommitAsync(ct);
        }
    }

    /// <inheritdoc/>
    public async Task<int> DeleteManyAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        var total = 0;
        foreach (var chunk in KeyValidator.ValidateAll(keys, _options.MaxKeyLength).Chunk(1000))
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            total += await conn.ExecuteAsync(
                $"DELETE FROM {_t} WHERE key = ANY(@Keys) AND {LiveWhere}",
                new { Keys = chunk }).ConfigureAwait(false);
        }
        return total;
    }

    // ── IExpiringRepository<TValue> ──────────────────────────────────────

    private sealed class ExpiresAtRow { public DateTime? ExpiresAt { get; init; } }

    /// <inheritdoc/>
    public async Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        // null row = key missing or expired; ExpiresAt = null = key lives forever
        var row = await conn.QueryFirstOrDefaultAsync<ExpiresAtRow>(
            $"SELECT expires_at AS \"ExpiresAt\" FROM {_t} WHERE key = @Key AND {LiveWhere}",
            new { Key = key }).ConfigureAwait(false);
        if (row is null) return null;
        if (row.ExpiresAt is null) return null;
        return new DateTimeOffset(row.ExpiresAt.Value, TimeSpan.Zero) - DateTimeOffset.UtcNow;
    }

    /// <inheritdoc/>
    public async Task<bool> SetTtlAsync(string key, TimeSpan? ttl, CancellationToken ct = default)
    {
        ValidateKey(key);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var rows = await conn.ExecuteAsync(
            $"UPDATE {_t} SET expires_at = @ExpiresAt, updated_at = @Now WHERE key = @Key AND {LiveWhere}",
            new
            {
                Key = key,
                ExpiresAt = ttl.HasValue ? (DateTimeOffset?)(DateTimeOffset.UtcNow + ttl.Value) : null,
                Now = DateTimeOffset.UtcNow
            }).ConfigureAwait(false);
        return rows > 0;
    }

    /// <inheritdoc/>
    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(
            $"DELETE FROM {_t} WHERE expires_at IS NOT NULL AND expires_at <= NOW()")
            .ConfigureAwait(false);
    }
}
