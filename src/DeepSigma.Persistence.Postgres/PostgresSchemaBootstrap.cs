using System.Data.Common;
using DeepSigma.DataAccess.RelationalDatabase;
using DeepSigma.Persistence.Core;

namespace DeepSigma.Persistence.Postgres;

internal static class PostgresSchemaBootstrap
{
    /// <summary>
    /// DDL for the <c>_migrations</c> tracking table required by <see cref="MigrationRunner"/>.
    /// Mirrors the package's internal constant of the same name (which is not yet public — see follow-up).
    /// Identifiers are unquoted so Postgres folds them to lowercase — matching <see cref="MigrationRunner"/>'s portable SQL.
    /// </summary>
    internal const string CreateMigrationsTableSql =
        "CREATE TABLE IF NOT EXISTS _migrations (Id TEXT PRIMARY KEY, AppliedAtUtc TIMESTAMPTZ NOT NULL)";

    public static void EnsureSchema(MigrationRunner runner, RelationalDatabaseApi db, PostgresOptions options)
    {
        var migrations = MigrationLoader
            .Load(typeof(PostgresSchemaBootstrap).Assembly, options.TableName)
            .Select(m => new Migration(m.Id, m.Sql))
            .ToList();

        if (options.AutoMigrate)
        {
            runner.ApplyAsync(migrations).GetAwaiter().GetResult();
            return;
        }

        var applied = TryListAppliedIds(db);
        if (applied is null)
            throw new SchemaVersionException(
                "Schema is uninitialized (no _migrations tracking table) but AutoMigrate is disabled. " +
                "Enable AutoMigrate or run MigrationRunner.ApplyAsync(...) at startup.");
        var pending = migrations.Where(m => !applied.Contains(m.Id)).Select(m => m.Id).ToList();
        if (pending.Count > 0)
            throw new SchemaVersionException(
                $"Schema is behind. Pending migrations: {string.Join(", ", pending)}. " +
                "Enable AutoMigrate or run MigrationRunner.ApplyAsync(...) at startup.");
    }

    /// <summary>
    /// Returns the set of already-applied migration ids, or <c>null</c> if the tracking table does not exist.
    /// Narrowed to <see cref="DbException"/> only — permission errors, OOM, and other non-Db failures propagate
    /// so they are not silently treated as "schema uninitialized."
    /// </summary>
    private static HashSet<string>? TryListAppliedIds(RelationalDatabaseApi db)
    {
        try
        {
            var ids = db.GetAllAsync<string>("SELECT id FROM _migrations").GetAwaiter().GetResult();
            return ids.ToHashSet(StringComparer.Ordinal);
        }
        catch (DbException)
        {
            // Most likely the table doesn't exist — Npgsql reports this as PostgresException
            // (a DbException) with SQLSTATE 42P01. Other DbException causes (e.g. wrong password)
            // would propagate as failures elsewhere; treating them as "uninitialized" here is the
            // tradeoff for not parsing SQLSTATE strings, which would couple us to Npgsql internals.
            return null;
        }
    }
}
