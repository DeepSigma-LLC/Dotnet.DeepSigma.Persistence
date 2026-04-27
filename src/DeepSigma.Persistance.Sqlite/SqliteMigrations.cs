using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;

namespace DeepSigma.Persistance.Sqlite;

internal static class SqliteMigrations
{
    private const int CurrentVersion = 1;
    private static readonly object Lock = new();

    public static void EnsureSchema(SqliteOptions options)
    {
        lock (Lock)
        {
            using var conn = new SqliteConnection(options.ConnectionString);
            conn.Open();
            conn.Execute("PRAGMA journal_mode=WAL;");

            conn.Execute(@"
                CREATE TABLE IF NOT EXISTS persistence_migrations (
                    version    INTEGER NOT NULL PRIMARY KEY,
                    applied_at TEXT NOT NULL
                )");

            var maxVersion = conn.QueryFirstOrDefault<int?>(
                "SELECT MAX(version) FROM persistence_migrations") ?? 0;

            if (!options.AutoMigrate)
            {
                if (maxVersion < CurrentVersion)
                    throw new SchemaVersionException(maxVersion, CurrentVersion);
                return;
            }

            var assembly = typeof(SqliteMigrations).Assembly;
            var resources = assembly.GetManifestResourceNames()
                .Where(n => n.Contains(".Migrations.") && n.EndsWith(".sql"))
                .Select(n =>
                {
                    var stem = Path.GetFileNameWithoutExtension(n.Split(".Migrations.")[1]);
                    return (Resource: n, Version: int.Parse(stem[..3]));
                })
                .Where(x => x.Version > maxVersion)
                .OrderBy(x => x.Version);

            foreach (var (resource, version) in resources)
            {
                using var stream = assembly.GetManifestResourceStream(resource)!;
                var sql = new StreamReader(stream, Encoding.UTF8).ReadToEnd()
                    .Replace("{TableName}", options.TableName);

                using var tx = conn.BeginTransaction();
                foreach (var stmt in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (!string.IsNullOrWhiteSpace(stmt))
                        conn.Execute(stmt, transaction: tx);

                conn.Execute(
                    "INSERT INTO persistence_migrations(version, applied_at) VALUES(@V, @At)",
                    new { V = version, At = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") },
                    tx);
                tx.Commit();
            }
        }
    }
}
