using DeepSigma.Persistence.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    /// Registers the default JSON serializer and repository implementation for SQLite persistence.
    /// If background purging is enabled in the options, a hosted purge service is also registered. Call this method
    /// during application startup to enable SQLite-backed data storage.
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

        // Register the default JSON serializer if the caller hasn't already supplied one.
        services.AddPersistenceCore();
        services.AddRepositoryImplementation(typeof(SqliteRepository<>));

        if (options.BackgroundPurge)
            services.AddHostedService<SqlitePurgeService>();

        return services;
    }
}
