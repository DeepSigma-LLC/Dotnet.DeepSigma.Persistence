using System.Text.Json;

namespace DeepSigma.Persistence.FileSystem;

/// <summary>
/// Purge logic for the file-system backend, factored out so the repository's
/// <see cref="FileSystemRepository{TValue}.PurgeExpiredAsync"/> and the hosted
/// <c>BackgroundPurgeService</c> share a single tested code path.
/// </summary>
/// <remarks>
/// Purge only needs <c>ExpiresAt</c>, so this uses a partial-deserialize shape — much faster than
/// reading the full envelope just to discard the value. Independent of <c>TValue</c>, which is why
/// it can live outside the generic repository class.
/// </remarks>
internal static class FileSystemPurge
{
    private sealed class ExpiryOnly
    {
        public DateTimeOffset? ExpiresAt { get; init; }
    }

    public static int PurgeExpired(FileSystemOptions options, CancellationToken ct = default)
    {
        if (!Directory.Exists(options.RootPath)) return 0;

        var now = DateTimeOffset.UtcNow;
        var count = 0;

        foreach (var shard in Directory.EnumerateDirectories(options.RootPath))
        {
            // Two-char shard directories only — see KeyHasher / FileSystemRepository.KeyPath.
            if (Path.GetFileName(shard).Length != 2) continue;

            foreach (var file in Directory.EnumerateFiles(shard, "*.json"))
            {
                ct.ThrowIfCancellationRequested();
                if (TryShouldPurge(file, now))
                {
                    try
                    {
                        File.Delete(file);
                        count++;
                    }
                    catch (IOException) { /* file vanished or is locked; next cycle picks it up */ }
                }
            }
        }
        return count;
    }

    private static bool TryShouldPurge(string path, DateTimeOffset now)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var info = JsonSerializer.Deserialize<ExpiryOnly>(fs);
            return info?.ExpiresAt is { } expiresAt && expiresAt <= now;
        }
        catch (IOException) { return false; }       // missing or transient lock
        catch (JsonException) { return false; }     // corrupt file — leave for human inspection
    }
}
