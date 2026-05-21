using System.Runtime.CompilerServices;
using AsyncKeyedLock;
using DeepSigma.Persistence.Core;

// Alias to disambiguate from System.Runtime.Serialization.SerializationException.
using SerializationException = DeepSigma.Persistence.SerializationException;

namespace DeepSigma.Persistence.FileSystem;

/// <summary>
/// File-per-key persistence backend. Single-process only — no cross-process file locking.
/// Practical scale: ~50k keys; consider SQLite beyond that.
/// </summary>
/// <remarks>
/// Reads and writes go through <see cref="IJsonValueSerializer"/> so a custom
/// <c>JsonValueSerializer</c> (with camelCase, custom converters, source-generated metadata, etc.)
/// registered in DI is honored here the same way it is on the SQL backends. The file format remains
/// UTF-8 JSON.
/// </remarks>
public sealed class FileSystemRepository<TValue> : IExpiringRepository<TValue>
{
    private readonly FileSystemOptions _options;
    private readonly IJsonValueSerializer _serializer;
    private readonly AsyncKeyedLocker<string> _locker = new();

    /// <summary>
    /// Initializes a new instance using DI-supplied dependencies.
    /// </summary>
    /// <param name="options">The file system options to configure the repository.</param>
    /// <param name="serializer">The JSON serializer used for envelope persistence.</param>
    public FileSystemRepository(FileSystemOptions options, IJsonValueSerializer serializer)
    {
        _options = options;
        _serializer = serializer;
        Directory.CreateDirectory(options.RootPath);
    }

    /// <summary>
    /// Convenience constructor for direct construction (e.g. tests). Uses the default JSON serializer.
    /// </summary>
    public FileSystemRepository(FileSystemOptions options)
        : this(options, new JsonValueSerializer()) { }

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

    private Envelope<TValue>? TryReadEnvelope(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return _serializer.Deserialize<Envelope<TValue>>(bytes);
        }
        catch (IOException) { return null; }
        catch (SerializationException ex)
        {
            throw new SerializationException($"Failed to deserialize envelope at '{path}'.", ex);
        }
    }

    private async Task<Envelope<TValue>?> TryReadEnvelopeAsync(string path, CancellationToken ct)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            return _serializer.Deserialize<Envelope<TValue>>(bytes);
        }
        catch (IOException) { return null; }
        catch (SerializationException ex)
        {
            throw new SerializationException($"Failed to deserialize envelope at '{path}'.", ex);
        }
    }

    private async Task WriteEnvelopeAsync(string path, Envelope<TValue> envelope, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = _serializer.Serialize(envelope);
        var tmp = path + ".tmp";
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
        {
            await fs.WriteAsync(bytes, ct).ConfigureAwait(false);
            await fs.FlushAsync(ct).ConfigureAwait(false);
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
    /// <inheritdoc/>

    public async Task<TValue?> GetAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var e = await TryReadEnvelopeAsync(KeyPath(key), ct);
        return e is null || e.IsExpired() ? default : e.Value;
    }

    /// <inheritdoc/>
    public async Task SetAsync(string key, TValue value, SetOptions? options = null, CancellationToken ct = default)
    {
        ValidateKey(key);
        var hash = KeyHasher.Hash(key);
        var path = KeyPath(key);
        var now = DateTimeOffset.UtcNow;
        using var _ = await _locker.LockAsync(hash, ct);
        var existing = await TryReadEnvelopeAsync(path, ct);
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

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var hash = KeyHasher.Hash(key);
        var path = KeyPath(key);
        using var _ = await _locker.LockAsync(hash, ct);
        var e = await TryReadEnvelopeAsync(path, ct);
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

    /// <inheritdoc/>
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
    /// <inheritdoc/>
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

    /// <inheritdoc/>
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
    /// <inheritdoc/>
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

    /// <inheritdoc/>
    /// <remarks>
    /// FileSystem has no batched write path — this method just iterates and calls <see cref="SetAsync"/>
    /// per key. It exists to satisfy the <see cref="IBulkRepository{TValue}"/> contract.
    /// </remarks>
    public async Task SetManyAsync(
        IEnumerable<KeyValuePair<string, TValue>> items,
        SetOptions? options = null,
        CancellationToken ct = default)
    {
        foreach (var (key, value) in items)
            await SetAsync(key, value, options, ct);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// FileSystem has no batched delete path — this method just iterates and calls <see cref="DeleteAsync"/>
    /// per key. It exists to satisfy the <see cref="IBulkRepository{TValue}"/> contract.
    /// </remarks>
    public async Task<int> DeleteManyAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        var count = 0;
        foreach (var key in keys)
            if (await DeleteAsync(key, ct)) count++;
        return count;
    }

    // ── IExpiringRepository<TValue> ──────────────────────────────────────
    /// <inheritdoc/>
    public async Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        var e = await TryReadEnvelopeAsync(KeyPath(key), ct);
        if (e is null || e.IsExpired()) return null;
        if (!e.ExpiresAt.HasValue) return null;
        return e.ExpiresAt.Value - DateTimeOffset.UtcNow;
    }

    /// <inheritdoc/>
    public async Task<bool> SetTtlAsync(string key, TimeSpan? ttl, CancellationToken ct = default)
    {
        ValidateKey(key);
        var hash = KeyHasher.Hash(key);
        var path = KeyPath(key);
        using var _ = await _locker.LockAsync(hash, ct);
        var existing = await TryReadEnvelopeAsync(path, ct);
        if (existing is null || existing.IsExpired()) return false;
        var updated = existing with
        {
            ExpiresAt = ttl.HasValue ? DateTimeOffset.UtcNow + ttl.Value : null,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await WriteEnvelopeAsync(path, updated, ct);
        return true;
    }

    /// <inheritdoc/>
    public Task<int> PurgeExpiredAsync(CancellationToken ct = default)
        => Task.FromResult(FileSystemPurge.PurgeExpired(_options, ct));
}
