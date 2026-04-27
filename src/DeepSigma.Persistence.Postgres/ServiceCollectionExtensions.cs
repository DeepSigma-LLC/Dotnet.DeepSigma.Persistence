using DeepSigma.Persistence.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

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
    /// This method registers the required services for using PostgreSQL as a persistence provider,
    /// including connection configuration and repository implementation. If background purging is enabled in the
    /// options, a hosted purge service is also registered. Call this method during application startup to enable
    /// PostgreSQL persistence features.
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
        services.TryAddSingleton(_ => NpgsqlDataSource.Create(options.ConnectionString));

        // Register the default JSON serializer if the caller hasn't already supplied one.
        services.AddPersistenceCore();
        services.AddRepositoryImplementation(typeof(PostgresRepository<>));

        if (options.BackgroundPurge)
            services.AddHostedService<PostgresPurgeService>();

        return services;
    }
}
