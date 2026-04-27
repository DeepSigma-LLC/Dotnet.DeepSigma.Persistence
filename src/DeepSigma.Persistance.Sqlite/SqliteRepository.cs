using System.Globalization;
using System.Runtime.CompilerServices;
using Dapper;
using DeepSigma.Persistance.Core;
using Microsoft.Data.Sqlite;

namespace DeepSigma.Persistance.Sqlite;

public sealed class SqliteRepository<TValue> : IExpiringRepository<TValue>
{
    private readonly SqliteOptions _options;
    private readonly IJsonValueSerializer _serializer;
    private readonly string _t; // table name shorthand

    public SqliteRepository(SqliteOptions options, IJsonValueSerializer serializer)
    {
        _options = options;
        _serializer = serializer;
        _t = options.TableName;
        SqliteMigrations.EnsureSchema(options);
    }

    /// <summary>Convenience constructor for testing; uses <see cref="JsonValueSerializer"/>.</summary>
    public SqliteRepository(SqliteOptions options) : this(options, new JsonValueSerializer()) { }

    private void ValidateKey(string key) => KeyValidator.Validate(key, _options.MaxKeyLength);

    private SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_options.ConnectionString);
        conn.Open();
        conn.Execute("PRAGMA journal_mode=WAL;");
        return conn;
    }

    // Millisecond precision so short TTLs compare correctly against strftime('%Y-%m-%d %H:%M:%f','now').
    private static string ToSqlite(DateTimeOffset dt) =>
        dt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff");

    private static string? ToSqlite(DateTimeOffset? dt) =>
        dt.HasValue ? ToSqlite(dt.Value) : null;

    // Use strftime with %f for subsecond precision — matches the .fff stored format.
    private static string LiveWhere =>
        "(expires_at IS NULL OR expires_at > strftime('%Y-%m-%d %H:%M:%f','now'))";

    // ── IRepository<TValue> ──────────────────────────────────────────────

    public async Task<TValue?> GetAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        using var conn = CreateConnection();
        var val = await conn.QueryFirstOrDefaultAsync<string>(
            $"SELECT value FROM {_t} WHERE key = @Key AND {LiveWhere}",
            new { Key = key }).ConfigureAwait(false);
        return val is null ? default : _serializer.DeserializeFromString<TValue>(val);
    }

    public async Task SetAsync(string key, TValue value, SetOptions? options = null, CancellationToken ct = default)
    {
        ValidateKey(key);
        using var conn = CreateConnection();
        var now = ToSqlite(DateTimeOffset.UtcNow);
        await conn.ExecuteAsync(
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
            }).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        using var conn = CreateConnection();
        var rows = await conn.ExecuteAsync(
            $"DELETE FROM {_t} WHERE key = @Key AND {LiveWhere}",
            new { Key = key }).ConfigureAwait(false);
        return rows > 0;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        using var conn = CreateConnection();
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
        using var conn = CreateConnection();
        var keys = await conn.QueryAsync<string>(
            $"SELECT key FROM {_t} WHERE {LiveWhere}{where} ORDER BY key",
            prefixValue is null ? null : new { Prefix = prefixValue })
            .ConfigureAwait(false);
        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();
            yield return key;
        }
    }

    public async IAsyncEnumerable<KeyValuePair<string, TValue>> ListAsync(
        string? prefix = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (where, prefixValue) = SqlPrefix.Build(prefix);
        using var conn = CreateConnection();
        var rows = await conn.QueryAsync<(string Key, string Value)>(
            $"SELECT key, value FROM {_t} WHERE {LiveWhere}{where} ORDER BY key",
            prefixValue is null ? null : new { Prefix = prefixValue })
            .ConfigureAwait(false);
        foreach (var (key, value) in rows)
        {
            ct.ThrowIfCancellationRequested();
            yield return new KeyValuePair<string, TValue>(key, _serializer.DeserializeFromString<TValue>(value)!);
        }
    }

    // ── IBulkRepository<TValue> ──────────────────────────────────────────

    public async Task<IReadOnlyDictionary<string, TValue>> GetManyAsync(
        IEnumerable<string> keys, CancellationToken ct = default)
    {
        var result = new Dictionary<string, TValue>();
        foreach (var chunk in KeyValidator.ValidateAll(keys, _options.MaxKeyLength).Chunk(500))
        {
            using var conn = CreateConnection();
            var rows = await conn.QueryAsync<(string Key, string Value)>(
                $"SELECT key, value FROM {_t} WHERE key IN @Keys AND {LiveWhere}",
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
        foreach (var chunk in items.Chunk(500))
        {
            using var conn = CreateConnection();
            using var tx = conn.BeginTransaction();
            foreach (var (key, value) in chunk)
            {
                ValidateKey(key);
                await conn.ExecuteAsync(sql,
                    new { Key = key, Value = _serializer.SerializeToString(value), Now = now, ExpiresAt = expiry }, tx)
                    .ConfigureAwait(false);
            }
            tx.Commit();
        }
    }

    public async Task<int> DeleteManyAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        var total = 0;
        foreach (var chunk in KeyValidator.ValidateAll(keys, _options.MaxKeyLength).Chunk(500))
        {
            using var conn = CreateConnection();
            total += await conn.ExecuteAsync(
                $"DELETE FROM {_t} WHERE key IN @Keys AND {LiveWhere}",
                new { Keys = chunk }).ConfigureAwait(false);
        }
        return total;
    }

    // ── IExpiringRepository<TValue> ──────────────────────────────────────

    // Used by GetTtlAsync. Defined as a class (not a value tuple) so Dapper's column-to-property
    // mapping is robust across versions and so `null` distinguishes "no row" from "no expiry."
    private sealed class ExpiresAtRow { public string? ExpiresAt { get; init; } }

    private const string SqliteTimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";

    public async Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        using var conn = CreateConnection();
        // null row     => key missing (or expired and filtered by LiveWhere)
        // ExpiresAt=null => key exists with no expiry
        var row = await conn.QueryFirstOrDefaultAsync<ExpiresAtRow>(
            $"SELECT expires_at AS ExpiresAt FROM {_t} WHERE key = @Key AND {LiveWhere}",
            new { Key = key }).ConfigureAwait(false);
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

    public async Task<bool> SetTtlAsync(string key, TimeSpan? ttl, CancellationToken ct = default)
    {
        ValidateKey(key);
        using var conn = CreateConnection();
        var rows = await conn.ExecuteAsync(
            $"UPDATE {_t} SET expires_at = @ExpiresAt, updated_at = @Now WHERE key = @Key AND {LiveWhere}",
            new
            {
                Key = key,
                ExpiresAt = ttl.HasValue ? ToSqlite(DateTimeOffset.UtcNow + ttl.Value) : null as string,
                Now = ToSqlite(DateTimeOffset.UtcNow)
            }).ConfigureAwait(false);
        return rows > 0;
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        return await conn.ExecuteAsync(
            $"DELETE FROM {_t} WHERE expires_at IS NOT NULL AND expires_at <= strftime('%Y-%m-%d %H:%M:%f','now')")
            .ConfigureAwait(false);
    }
}
