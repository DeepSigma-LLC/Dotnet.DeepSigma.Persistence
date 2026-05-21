using DeepSigma.Persistence.SqlServer;
using DeepSigma.Persistence.Testing;
using Testcontainers.MsSql;
using Xunit;

namespace DeepSigma.Persistence.SqlServer.Tests;

public sealed class SqlServerRepositoryTests
    : ExpiringRepositoryContractTests<SqlServerRepository<string>>, IAsyncLifetime
{
    private MsSqlContainer _container = null!;
    private int _tableCounter;

    // Container round-trips need more time than in-memory or local SQLite.
    protected override TimeSpan ShortTtl => TimeSpan.FromMilliseconds(500);
    protected override TimeSpan ExpiryBuffer => TimeSpan.FromMilliseconds(1500);

    // SQL Server's clustered-index entry max is 900 bytes; NVARCHAR(450) is exactly 900 bytes,
    // so SqlServerOptions defaults MaxKeyLength to 450. The contract suite's
    // SetAsync_KeyAtMaxLength_Succeeds reads this property to match.
    protected override int MaxKeyLength => 450;

    public async ValueTask InitializeAsync()
    {
        // Testcontainers.MsSql 4.x requires an explicit image — the parameterless ctor was deprecated.
        // mcr.microsoft.com/mssql/server:2022-latest is the current Microsoft-published LTS tag.
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();
    }

    protected override SqlServerRepository<string> CreateRepository()
    {
        var tableName = $"test_{Interlocked.Increment(ref _tableCounter):D4}";
        var options = new SqlServerOptions
        {
            ConnectionString = _container.GetConnectionString(),
            TableName = tableName,
            AutoMigrate = true,
        };
        return new SqlServerRepository<string>(options);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
