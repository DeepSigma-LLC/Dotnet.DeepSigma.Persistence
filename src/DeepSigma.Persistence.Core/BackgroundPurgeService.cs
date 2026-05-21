using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepSigma.Persistence.Core;

/// <summary>
/// Hosted service that runs a backend-supplied purge action on a fixed interval.
/// </summary>
/// <remarks>
/// Purge is a storage-level concern — one table or one directory holds entries of every registered
/// <c>TValue</c> — so the cleanup callback is not generic. Each backend's DI extension constructs
/// this service with a lambda that performs the actual delete (raw SQL for the relational backends,
/// a file walk for FileSystem). Failures are caught and logged so a transient error doesn't tear
/// down the host.
/// </remarks>
public sealed class BackgroundPurgeService : BackgroundService
{
    private readonly Func<CancellationToken, Task<int>> _purge;
    private readonly TimeSpan _interval;
    private readonly ILogger<BackgroundPurgeService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackgroundPurgeService"/>.
    /// </summary>
    /// <param name="purge">The backend-specific purge action. Returns the number of entries removed.</param>
    /// <param name="interval">How long to wait between purge cycles. Must be positive.</param>
    /// <param name="logger">Optional logger. Defaults to <see cref="NullLogger{T}.Instance"/>.</param>
    public BackgroundPurgeService(
        Func<CancellationToken, Task<int>> purge,
        TimeSpan interval,
        ILogger<BackgroundPurgeService>? logger = null)
    {
        _purge = purge;
        _interval = interval;
        _logger = logger ?? NullLogger<BackgroundPurgeService>.Instance;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var purged = await _purge(stoppingToken).ConfigureAwait(false);
                if (purged > 0)
                    _logger.LogDebug("Purged {Count} expired entries.", purged);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown — let the loop exit on the next WaitForNextTickAsync.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Purge cycle failed; will retry next interval.");
            }
        }
    }
}
