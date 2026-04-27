using DeepSigma.Persistence.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace DeepSigma.Persistence.Postgres;

public static class ServiceCollectionExtensions
{
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
