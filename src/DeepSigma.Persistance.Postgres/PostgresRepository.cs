using System.Runtime.CompilerServices;
using Dapper;
using DeepSigma.Persistance.Core;
using Npgsql;

namespace DeepSigma.Persistance.Postgres;

public sealed class PostgresRepository<TValue> : IExpiringRepository<TValue>
{
    private readonly PostgresOptions _options;
    private readonly IJsonValueSerializer _serializer;
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _t;

    public PostgresRepository(NpgsqlDataSource dataSource, PostgresOptions options, IJsonValueSerializer serializer)
    {
        _options = options;
        _serializer = serializer;
        _dataSource = dataSource;
        _t = options.TableName;
        PostgresMigrations.EnsureSchema(options);
    }

    /// <summary>Convenience constructor for testing; uses <see cref="JsonValueSerializer"/>.</summary>
    public PostgresRepository(NpgsqlDataSource dataSource, PostgresOptions options)
        : this(dataSource, options, new JsonValueSerializer()) { }

    private void ValidateKey(string key) => KeyValidator.Validate(key, _options.MaxKeyLength);

    private static string LiveWhere =>
        "(expires_at IS NULL OR expires_at > NOW())";

    // ── IRepository<TValue> ──────────────────────────────────────────────

    public async Task<TValue?> GetAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var val = await conn.QueryFirstOrDefaultAsync<string>(
            $"SELECT value::text FROM {_t} WHERE key = @Key AND {LiveWhere}",
            new { Key = key }).ConfigureAwait(false);
        return val is null ? default : _serializer.DeserializeFromString<TValue>(val);
    }

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

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var rows = await conn.ExecuteAsync(
            $"DELETE FROM {_t} WHERE key = @Key AND {LiveWhere}",
            new { Key = key }).ConfigureAwait(false);
        return rows > 0;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var count = await conn.QueryFirstAsync<int>(
            $"SELECT COUNT(1) FROM {_t} WHERE key = @Key AND {LiveWhere}",
            new { Key = key }).ConfigureAwait(false);
        return count > 0;
    }

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

    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(
            $"DELETE FROM {_t} WHERE expires_at IS NOT NULL AND expires_at <= NOW()")
            .ConfigureAwait(false);
    }
}
