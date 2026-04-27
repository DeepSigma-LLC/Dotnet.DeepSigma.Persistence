using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace DeepSigma.Persistence.FileSystem;

internal sealed class FileSystemPurgeService : BackgroundService
{
    private readonly FileSystemOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemPurgeService"/> class with the specified <see cref="FileSystemOptions"/>.
    /// </summary>
    /// <param name="options">The <see cref="FileSystemOptions"/> to use for configuring the purge service.</param>
    public FileSystemPurgeService(FileSystemOptions options) => _options = options;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PurgeInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            PurgeExpired();
    }

    private void PurgeExpired()
    {
        if (!Directory.Exists(_options.RootPath)) return;
        var now = DateTimeOffset.UtcNow;
        foreach (var shard in Directory.EnumerateDirectories(_options.RootPath))
        {
            var name = Path.GetFileName(shard);
            if (name.Length != 2) continue;
            foreach (var file in Directory.EnumerateFiles(shard, "*.json"))
            {
                try
                {
                    using var fs = File.OpenRead(file);
                    var info = JsonSerializer.Deserialize<ExpiryOnly>(fs);
                    if (info?.ExpiresAt.HasValue == true && info.ExpiresAt.Value <= now)
                        File.Delete(file);
                }
                catch { }
            }
        }
    }

    private sealed class ExpiryOnly { public DateTimeOffset? ExpiresAt { get; init; } }
}
