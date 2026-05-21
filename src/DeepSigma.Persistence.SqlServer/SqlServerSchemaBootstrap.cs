using System.Data.Common;
using DeepSigma.DataAccess.RelationalDatabase;
using DeepSigma.Persistence.Core;

namespace DeepSigma.Persistence.SqlServer;

internal static class SqlServerSchemaBootstrap
{
    /// <summary>
    /// DDL for the <c>_migrations</c> tracking table required by <see cref="MigrationRunner"/>.
    /// Mirrors the package's internal constant of the same name (which is not yet public — see follow-up).
    /// Always created in <c>dbo</c> regardless of the per-instance <see cref="SqlServerOptions.Schema"/>,
    /// because migration bookkeeping is process-wide rather than instance-scoped.
    /// </summary>
    internal const string CreateMigrationsTableSql =
        "IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'_migrations') " +
        "CREATE TABLE [dbo].[_migrations] ([Id] NVARCHAR(450) NOT NULL PRIMARY KEY, [AppliedAtUtc] DATETIMEOFFSET NOT NULL)";

    public static void EnsureSchema(MigrationRunner runner, RelationalDatabaseApi db, SqlServerOptions options)
    {
        // MigrationLoader only substitutes {TableName}; do {Schema} ourselves so the loader's signature
        // stays simple and dependency-free.
        var migrations = MigrationLoader
            .Load(typeof(SqlServerSchemaBootstrap).Assembly, options.TableName)
            .Select(m => new Migration(m.Id, m.Sql.Replace("{Schema}", options.Schema)))
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
            var ids = db.GetAllAsync<string>("SELECT [Id] FROM [dbo].[_migrations]").GetAwaiter().GetResult();
            return ids.ToHashSet(StringComparer.Ordinal);
        }
        catch (DbException)
        {
            // Most likely the table doesn't exist — SqlException (a DbException) reports error 208
            // "Invalid object name". Other DbException causes (e.g. login failure) would propagate
            // elsewhere; treating them as "uninitialized" here is the same tradeoff used for Postgres.
            return null;
        }
    }
}
