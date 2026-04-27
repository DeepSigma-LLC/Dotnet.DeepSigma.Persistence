using Dapper;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace DeepSigma.Persistence.Postgres;

internal sealed class PostgresPurgeService : BackgroundService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgresOptions _options;

    public PostgresPurgeService(NpgsqlDataSource dataSource, PostgresOptions options)
    {
        _dataSource = dataSource;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PurgeInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await using var conn = await _dataSource.OpenConnectionAsync(stoppingToken);
            await conn.ExecuteAsync(
                $"DELETE FROM {_options.TableName} WHERE expires_at IS NOT NULL AND expires_at <= NOW()")
                .ConfigureAwait(false);
        }
    }
}
