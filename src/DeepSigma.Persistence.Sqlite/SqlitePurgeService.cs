using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;

namespace DeepSigma.Persistence.Sqlite;

internal sealed class SqlitePurgeService : BackgroundService
{
    private readonly SqliteOptions _options;

    public SqlitePurgeService(SqliteOptions options) => _options = options;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PurgeInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            using var conn = new SqliteConnection(_options.ConnectionString);
            conn.Open();
            await conn.ExecuteAsync(
                $"DELETE FROM {_options.TableName} WHERE expires_at IS NOT NULL AND expires_at <= strftime('%Y-%m-%d %H:%M:%f','now')")
                .ConfigureAwait(false);
        }
    }
}
