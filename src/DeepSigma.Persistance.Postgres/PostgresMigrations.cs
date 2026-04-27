using System.Text;
using Dapper;
using Npgsql;

namespace DeepSigma.Persistence.Postgres;

internal static class PostgresMigrations
{
    private const int CurrentVersion = 1;
    private static readonly object Lock = new();
    private static readonly HashSet<string> Migrated = [];

    public static void EnsureSchema(PostgresOptions options)
    {
        var cacheKey = $"{options.ConnectionString}|{options.TableName}";
        lock (Lock)
        {
            if (Migrated.Contains(cacheKey)) return;

            using var conn = new NpgsqlConnection(options.ConnectionString);
            conn.Open();

            conn.Execute(@"
                CREATE TABLE IF NOT EXISTS persistence_migrations (
                    version    INT PRIMARY KEY,
                    applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                )");

            var maxVersion = conn.QueryFirstOrDefault<int?>(
                "SELECT MAX(version) FROM persistence_migrations") ?? 0;

            if (!options.AutoMigrate)
            {
                if (maxVersion < CurrentVersion)
                    throw new SchemaVersionException(maxVersion, CurrentVersion);
                Migrated.Add(cacheKey);
                return;
            }

            var assembly = typeof(PostgresMigrations).Assembly;
            var pending = assembly.GetManifestResourceNames()
                .Where(n => n.Contains(".Migrations.") && n.EndsWith(".sql"))
                .Select(n =>
                {
                    var stem = Path.GetFileNameWithoutExtension(n.Split(".Migrations.")[1]);
                    return (Resource: n, Version: int.Parse(stem[..3]));
                })
                .Where(x => x.Version > maxVersion)
                .OrderBy(x => x.Version);

            foreach (var (resource, version) in pending)
            {
                using var stream = assembly.GetManifestResourceStream(resource)!;
                var sql = new StreamReader(stream, Encoding.UTF8).ReadToEnd()
                    .Replace("{TableName}", options.TableName);

                using var tx = conn.BeginTransaction();
                foreach (var stmt in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (!string.IsNullOrWhiteSpace(stmt))
                        conn.Execute(stmt, transaction: tx);

                conn.Execute(
                    "INSERT INTO persistence_migrations(version) VALUES(@V)",
                    new { V = version }, tx);
                tx.Commit();
            }

            Migrated.Add(cacheKey);
        }
    }
}
