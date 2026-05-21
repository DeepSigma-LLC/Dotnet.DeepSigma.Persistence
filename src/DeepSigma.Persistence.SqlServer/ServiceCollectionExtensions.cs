using DeepSigma.DataAccess.RelationalDatabase;
using DeepSigma.Persistence.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DeepSigma.Persistence.SqlServer;

/// <summary>
/// Extension methods for registering Microsoft SQL Server persistence services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds SQL Server-based persistence services and configuration to the specified service collection.
    /// </summary>
    /// <remarks>
    /// Registers the underlying <c>DeepSigma.DataAccess.SqlServer</c> provider (connection factory,
    /// <c>RelationalDatabaseApi</c>, schema service, and <c>MigrationRunner</c>), the default JSON
    /// serializer, and the repository implementation. If background purging is enabled in the options,
    /// a hosted purge service is also registered.
    /// </remarks>
    /// <param name="services">The service collection to which the persistence services will be added. Cannot be null.</param>
    /// <param name="configure">A delegate to configure the SQL Server persistence options. Cannot be null.</param>
    /// <returns>The same service collection instance, enabling method chaining.</returns>
    public static IServiceCollection AddSqlServerPersistence(
        this IServiceCollection services,
        Action<SqlServerOptions> configure)
    {
        var options = new SqlServerOptions { ConnectionString = "" };
        configure(options);
        options.Validate();
        services.TryAddSingleton(options);

        // Registers SqlServerConnectionFactory, RelationalDatabaseApi, SqlServerSchemaService,
        // and MigrationRunner.
        services.AddDeepSigmaSqlServer(options.ConnectionString);

        services.AddPersistenceCore();
        services.AddRepositoryImplementation(typeof(SqlServerRepository<>));

        if (options.BackgroundPurge)
        {
            services.AddHostedService(sp =>
            {
                var db = sp.GetRequiredService<RelationalDatabaseApi>();
                var opts = sp.GetRequiredService<SqlServerOptions>();
                return new BackgroundPurgeService(
                    ct => db.ExecuteAsync(
                        $"DELETE FROM [{opts.Schema}].[{opts.TableName}] WHERE [expires_at] IS NOT NULL AND [expires_at] <= SYSDATETIMEOFFSET()",
                        cancellationToken: ct),
                    opts.PurgeInterval,
                    sp.GetService<ILogger<BackgroundPurgeService>>());
            });
        }

        return services;
    }
}
