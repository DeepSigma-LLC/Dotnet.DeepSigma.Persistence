using DeepSigma.DataAccess.RelationalDatabase;
using DeepSigma.Persistence.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DeepSigma.Persistence.Postgres;

/// <summary>
/// Extension methods for registering Postgres persistence services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds PostgreSQL-based persistence services and configuration to the specified service collection.
    /// </summary>
    /// <remarks>
    /// Registers the underlying <c>DeepSigma.DataAccess.Postgres</c> provider (connection factory backed by
    /// <c>NpgsqlDataSource</c>, <c>RelationalDatabaseApi</c>, schema service, bulk copier, and
    /// <c>MigrationRunner</c>), the default JSON serializer, and the repository implementation. If background
    /// purging is enabled in the options, a hosted purge service is also registered.
    /// </remarks>
    /// <param name="services">The service collection to which the persistence services will be added. Cannot be null.</param>
    /// <param name="configure">A delegate to configure the PostgreSQL persistence options. Cannot be null.</param>
    /// <returns>The same service collection instance, enabling method chaining.</returns>
    public static IServiceCollection AddPostgresPersistence(
        this IServiceCollection services,
        Action<PostgresOptions> configure)
    {
        var options = new PostgresOptions { ConnectionString = "" };
        configure(options);
        options.Validate();
        services.TryAddSingleton(options);

        // Registers PostgresConnectionFactory (with owned NpgsqlDataSource), RelationalDatabaseApi,
        // PostgresSchemaService, PostgresBulkCopier, and MigrationRunner.
        services.AddDeepSigmaPostgres(options.ConnectionString);

        services.AddPersistenceCore();
        services.AddRepositoryImplementation(typeof(PostgresRepository<>));

        if (options.BackgroundPurge)
        {
            services.AddHostedService(sp =>
            {
                var db = sp.GetRequiredService<RelationalDatabaseApi>();
                var opts = sp.GetRequiredService<PostgresOptions>();
                return new BackgroundPurgeService(
                    ct => db.ExecuteAsync(
                        $"DELETE FROM {opts.TableName} WHERE expires_at IS NOT NULL AND expires_at <= NOW()",
                        cancellationToken: ct),
                    opts.PurgeInterval,
                    sp.GetService<ILogger<BackgroundPurgeService>>());
            });
        }

        return services;
    }
}
