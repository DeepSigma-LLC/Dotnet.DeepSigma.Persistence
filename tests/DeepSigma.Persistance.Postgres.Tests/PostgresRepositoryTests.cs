using DeepSigma.Persistance.Postgres;
using DeepSigma.Persistance.Testing;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace DeepSigma.Persistance.Postgres.Tests;

public sealed class PostgresRepositoryTests
    : ExpiringRepositoryContractTests<PostgresRepository<string>>, IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private NpgsqlDataSource _dataSource = null!;
    private int _tableCounter;

    // Container round-trips need more time than in-memory or local SQLite.
    protected override TimeSpan ShortTtl => TimeSpan.FromMilliseconds(500);
    protected override TimeSpan ExpiryBuffer => TimeSpan.FromMilliseconds(1500);

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder().Build();
        await _container.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
    }

    protected override PostgresRepository<string> CreateRepository()
    {
        var tableName = $"test_{Interlocked.Increment(ref _tableCounter):D4}";
        var options = new PostgresOptions
        {
            ConnectionString = _container.GetConnectionString(),
            TableName = tableName,
            AutoMigrate = true
        };
        return new PostgresRepository<string>(_dataSource, options);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }
}
