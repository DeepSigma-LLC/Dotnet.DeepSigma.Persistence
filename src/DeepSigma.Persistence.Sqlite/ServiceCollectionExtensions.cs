using DeepSigma.DataAccess.RelationalDatabase;
using DeepSigma.Persistence.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DeepSigma.Persistence.Sqlite;

/// <summary>
/// Extension methods for registering SQLite persistence services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds SQLite-based persistence services and repositories to the specified service collection.
    /// </summary>
    /// <remarks>
    /// Registers the underlying <c>DeepSigma.DataAccess.Sqlite</c> provider (connection factory,
    /// <c>RelationalDatabaseApi</c>, schema service, and <c>MigrationRunner</c>), the default JSON
    /// serializer, and the repository implementation for SQLite persistence. If background purging
    /// is enabled in the options, a hosted purge service is also registered. Call this method during
    /// application startup to enable SQLite-backed data storage.
    /// </remarks>
    /// <param name="services">The service collection to which the persistence services will be added.</param>
    /// <param name="configure">A delegate to configure the options for SQLite persistence. This action is required to set up the connection
    /// string and other options.</param>
    /// <returns>The same service collection instance, enabling method chaining.</returns>
    public static IServiceCollection AddSqlitePersistence(
        this IServiceCollection services,
        Action<SqliteOptions> configure)
    {
        var options = new SqliteOptions { ConnectionString = "" };
        configure(options);
        options.Validate();
        services.TryAddSingleton(options);

        // Registers SqliteConnectionFactory, RelationalDatabaseApi, SqliteSchemaService, and MigrationRunner.
        // The onConnectionOpened callback applies per-connection PRAGMAs.
        services.AddDeepSigmaSqlite(options.ConnectionString, SqliteSchemaBootstrap.ApplyConnectionPragmas);

        services.AddPersistenceCore();
        services.AddRepositoryImplementation(typeof(SqliteRepository<>));

        if (options.BackgroundPurge)
        {
            services.AddHostedService(sp =>
            {
                var db = sp.GetRequiredService<RelationalDatabaseApi>();
                var opts = sp.GetRequiredService<SqliteOptions>();
                return new BackgroundPurgeService(
                    ct => db.ExecuteAsync(
                        $"DELETE FROM {opts.TableName} WHERE expires_at IS NOT NULL AND expires_at <= strftime('%Y-%m-%d %H:%M:%f','now')",
                        cancellationToken: ct),
                    opts.PurgeInterval,
                    sp.GetService<ILogger<BackgroundPurgeService>>());
            });
        }

        return services;
    }
}
