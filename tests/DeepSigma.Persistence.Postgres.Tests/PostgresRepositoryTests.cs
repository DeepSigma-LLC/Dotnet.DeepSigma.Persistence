using DeepSigma.Persistence.Postgres;
using DeepSigma.Persistence.Testing;
using Testcontainers.PostgreSql;
using Xunit;

namespace DeepSigma.Persistence.Postgres.Tests;

public sealed class PostgresRepositoryTests
    : ExpiringRepositoryContractTests<PostgresRepository<string>>, IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private int _tableCounter;

    // Container round-trips need more time than in-memory or local SQLite.
    protected override TimeSpan ShortTtl => TimeSpan.FromMilliseconds(500);
    protected override TimeSpan ExpiryBuffer => TimeSpan.FromMilliseconds(1500);

    public async ValueTask InitializeAsync()
    {
        _container = new PostgreSqlBuilder().Build();
        await _container.StartAsync();
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
        return new PostgresRepository<string>(options);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
