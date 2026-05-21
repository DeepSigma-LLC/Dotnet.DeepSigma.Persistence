using System.Data.Common;
using DeepSigma.DataAccess.RelationalDatabase;
using DeepSigma.Persistence.Core;
using Microsoft.Data.Sqlite;

namespace DeepSigma.Persistence.Sqlite;

internal static class SqliteSchemaBootstrap
{
    /// <summary>
    /// DDL for the <c>_migrations</c> tracking table required by <see cref="MigrationRunner"/>.
    /// Mirrors the package's internal constant of the same name (which is not yet public — see follow-up).
    /// Shape must satisfy: <c>Id</c> string PK + <c>AppliedAtUtc</c> timestamp.
    /// </summary>
    internal const string CreateMigrationsTableSql =
        "CREATE TABLE IF NOT EXISTS _migrations (Id TEXT PRIMARY KEY, AppliedAtUtc TEXT NOT NULL)";

    /// <summary>
    /// Per-connection PRAGMAs. <c>foreign_keys</c> and <c>busy_timeout</c> reset on every new connection,
    /// so they must be set in the <c>onConnectionOpened</c> callback (every time the pool hands out a
    /// connection). <c>journal_mode=WAL</c> is durable per database file and is set once via
    /// <see cref="EnableWalMode"/> at startup — issuing it on every open is just wasted round-trips.
    /// </summary>
    public static void ApplyConnectionPragmas(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "PRAGMA foreign_keys=ON;" +
            "PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// One-shot: switch the database to WAL journal mode. WAL persists across connections (it's a
    /// property of the database file, not the connection), so this only needs to run once at startup.
    /// </summary>
    private static void EnableWalMode(RelationalDatabaseApi db) =>
        db.ExecuteAsync("PRAGMA journal_mode=WAL;").GetAwaiter().GetResult();

    public static void EnsureSchema(MigrationRunner runner, RelationalDatabaseApi db, SqliteOptions options)
    {
        EnableWalMode(db);

        var migrations = MigrationLoader
            .Load(typeof(SqliteSchemaBootstrap).Assembly, options.TableName)
            .Select(m => new Migration(m.Id, m.Sql))
            .ToList();

        if (options.AutoMigrate)
        {
            runner.ApplyAsync(migrations).GetAwaiter().GetResult();
            return;
        }

        // AutoMigrate=false: verify all expected migration ids are recorded; otherwise refuse to start.
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
            var ids = db.GetAllAsync<string>("SELECT Id FROM _migrations").GetAwaiter().GetResult();
            return ids.ToHashSet(StringComparer.Ordinal);
        }
        catch (DbException)
        {
            // Most likely the table doesn't exist. SQLite reports this via SqliteException (a DbException),
            // typically with "no such table: _migrations" — but we cannot reliably parse error text across
            // SQLite versions, so any DbException here is treated as "uninitialized."
            return null;
        }
    }
}
