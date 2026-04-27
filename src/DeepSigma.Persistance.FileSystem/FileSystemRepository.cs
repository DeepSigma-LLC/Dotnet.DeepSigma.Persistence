using System.Runtime.CompilerServices;
using System.Text.Json;
using AsyncKeyedLock;
using DeepSigma.Persistance.Core;

// Alias to disambiguate from System.Runtime.Serialization.SerializationException.
using SerializationException = DeepSigma.Persistance.SerializationException;

namespace DeepSigma.Persistance.FileSystem;

/// <summary>
/// File-per-key persistence backend. Single-process only — no cross-process file locking.
/// Practical scale: ~50k keys; consider SQLite beyond that.
/// </summary>
public sealed class FileSystemRepository<TValue> : IExpiringRepository<TValue>
{
    private readonly FileSystemOptions _options;
    private readonly AsyncKeyedLocker<string> _locker = new();

    public FileSystemRepository(FileSystemOptions options)
    {
        _options = options;
        Directory.CreateDirectory(options.RootPath);
    }

    private void ValidateKey(string key) => KeyValidator.Validate(key, _options.MaxKeyLength);

    private string KeyPath(string key)
    {
        var hash = KeyHasher.Hash(key);
        return Path.Combine(_options.RootPath, hash[..2], hash + ".json");
    }

    // FileNotFoundException and DirectoryNotFoundException both derive from IOException —
    // catching IOException covers "file missing" and "transient lock during atomic move".
    // JsonException is rethrown as SerializationException so callers can handle it backend-agnostically.
    // Anything else (UnauthorizedAccessException, OutOfMemoryException, OperationCanceledException) propagates.

    private static Envelope<TValue>? TryReadEnvelope(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return JsonSerializer.Deserialize<Envelope<TValue>>(fs);
        }
        catch (IOException) { return null; }
        catch (JsonException ex)
        {
            throw new SerializationException($"Failed to deserialize envelope at '{path}'.", ex);
        }
    }

    private static async Task<Envelope<TValue>?> TryReadEnvelopeAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            return await JsonSerializer.DeserializeAsync<Envelope<TValue>>(fs, cancellationToken: ct);
        }
        catch (IOException) { return null; }
        catch (JsonException ex)
        {
            throw new SerializationException($"Failed to deserialize envelope at '{path}'.", ex);
        }
    }

    private static async Task WriteEnvelopeAsync(string path, Envelope<TValue> envelope, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
        {
            await JsonSerializer.SerializeAsync(fs, envelope, cancellationToken: ct);
            await fs.FlushAsync(ct);
        }
        File.Move(tmp, path, overwrite: true);
    }

    private IEnumerable<string> EnumerateFiles()
    {
        if (!Directory.Exists(_options.RootPath)) yield break;
        foreach (var shard in Directory.EnumerateDirectories(_options.RootPath))
        {
            var name = Path.GetFileName(shard);
            if (name.Length != 2) continue;
            foreach (var file in Directory.EnumerateFiles(shard, "*.json"))
                yield return file;
        }
    }

    // ── IRepository<TValue> ──────────────────────────────────────────────

    public async Task<TValue?> GetAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var e = await TryReadEnvelopeAsync(KeyPath(key), ct);
        return e is null || e.IsExpired() ? default : e.Value;
    }

    public async Task SetAsync(string key, TValue value, SetOptions? options = null, CancellationToken ct = default)
    {
        ValidateKey(key);
        var hash = KeyHasher.Hash(key);
        var path = KeyPath(key);
        var now = DateTimeOffset.UtcNow;
        using var _ = await _locker.LockAsync(hash, ct);
        var existing = TryReadEnvelope(path);
        var envelope = new Envelope<TValue>
        {
            Key = key,
            Value = value,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
            ExpiresAt = options?.ComputeExpiry()
        };
        await WriteEnvelopeAsync(path, envelope, ct);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var hash = KeyHasher.Hash(key);
        var path = KeyPath(key);
        using var _ = await _locker.LockAsync(hash, ct);
        var e = TryReadEnvelope(path);
        if (e is null || e.IsExpired())
        {
            TryDeleteIgnoringTransient(path);
            return false;
        }
        File.Delete(path);
        return true;
    }

    /// <summary>
    /// Best-effort delete that swallows missing-file and locked-file errors only.
    /// Permission errors and other failures still propagate.
    /// </summary>
    private static void TryDeleteIgnoringTransient(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* missing or in-use; nothing to do */ }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var e = await TryReadEnvelopeAsync(KeyPath(key), ct);
        return e is not null && !e.IsExpired();
    }

    // List/Bulk operations skip individual corrupt files (consistent with PurgeExpiredAsync) so
    // a single bad file doesn't poison the entire enumeration. Targeted reads (GetAsync, etc.)
    // still throw SerializationException so the caller is told about the problem.

#pragma warning disable CS1998
    public async IAsyncEnumerable<string> ListKeysAsync(
        string? prefix = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var path in EnumerateFiles())
        {
            ct.ThrowIfCancellationRequested();
            Envelope<TValue>? e;
            try { e = TryReadEnvelope(path); }
            catch (SerializationException) { continue; }
            if (e is null || e.IsExpired()) continue;
            if (prefix is not null && !e.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            yield return e.Key;
        }
    }

    public async IAsyncEnumerable<KeyValuePair<string, TValue>> ListAsync(
        string? prefix = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var path in EnumerateFiles())
        {
            ct.ThrowIfCancellationRequested();
            Envelope<TValue>? e;
            try { e = TryReadEnvelope(path); }
            catch (SerializationException) { continue; }
            if (e is null || e.IsExpired()) continue;
            if (prefix is not null && !e.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            yield return new KeyValuePair<string, TValue>(e.Key, e.Value);
        }
    }
#pragma warning restore CS1998

    // ── IBulkRepository<TValue> ──────────────────────────────────────────

    public async Task<IReadOnlyDictionary<string, TValue>> GetManyAsync(
        IEnumerable<string> keys, CancellationToken ct = default)
    {
        var result = new Dictionary<string, TValue>();
        foreach (var key in keys)
        {
            ValidateKey(key);
            Envelope<TValue>? e;
            try { e = await TryReadEnvelopeAsync(KeyPath(key), ct); }
            catch (SerializationException) { continue; }   // bulk: corrupt entries are skipped, like missing keys
            if (e is not null && !e.IsExpired())
                result[key] = e.Value;
        }
        return result;
    }

    public async Task SetManyAsync(
        IEnumerable<KeyValuePair<string, TValue>> items,
        SetOptions? options = null,
        CancellationToken ct = default)
    {
        foreach (var (key, value) in items)
            await SetAsync(key, value, options, ct);
    }

    public async Task<int> DeleteManyAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        var count = 0;
        foreach (var key in keys)
            if (await DeleteAsync(key, ct)) count++;
        return count;
    }

    // ── IExpiringRepository<TValue> ──────────────────────────────────────

    public async Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var e = await TryReadEnvelopeAsync(KeyPath(key), ct);
        if (e is null || e.IsExpired()) return null;
        if (!e.ExpiresAt.HasValue) return null;
        return e.ExpiresAt.Value - DateTimeOffset.UtcNow;
    }

    public async Task<bool> SetTtlAsync(string key, TimeSpan? ttl, CancellationToken ct = default)
    {
        ValidateKey(key);
        var hash = KeyHasher.Hash(key);
        var path = KeyPath(key);
        using var _ = await _locker.LockAsync(hash, ct);
        var existing = TryReadEnvelope(path);
        if (existing is null || existing.IsExpired()) return false;
        var updated = existing with
        {
            ExpiresAt = ttl.HasValue ? DateTimeOffset.UtcNow + ttl.Value : null,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await WriteEnvelopeAsync(path, updated, ct);
        return true;
    }

    public Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        var count = 0;
        foreach (var path in EnumerateFiles())
        {
            ct.ThrowIfCancellationRequested();

            // Skip files we can't decode rather than aborting the entire purge — a single corrupt
            // file shouldn't prevent the rest from being cleaned up.
            Envelope<TValue>? e;
            try { e = TryReadEnvelope(path); }
            catch (SerializationException) { continue; }

            if (e is null || !e.IsExpired()) continue;
            try
            {
                File.Delete(path);
                count++;
            }
            catch (IOException) { /* file vanished or is locked; try again next purge cycle */ }
        }
        return Task.FromResult(count);
    }
}
