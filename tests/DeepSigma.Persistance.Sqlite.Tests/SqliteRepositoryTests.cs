using DeepSigma.Persistance.Sqlite;
using DeepSigma.Persistance.Testing;

namespace DeepSigma.Persistance.Sqlite.Tests;

/// <summary>
/// Runs the full contract suite against SqliteRepository.
/// Each test gets a fresh temp database file so there is no state bleed between tests.
/// </summary>
public sealed class SqliteRepositoryTests
    : ExpiringRepositoryContractTests<SqliteRepository<string>>, IDisposable
{
    private readonly List<string> _tempFiles = [];

    protected override SqliteRepository<string> CreateRepository()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ds-sqlite-{Guid.NewGuid():N}.db");
        _tempFiles.Add(path);
        return new SqliteRepository<string>(new SqliteOptions
        {
            ConnectionString = $"Data Source={path}",
            AutoMigrate = true
        });
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }
}
