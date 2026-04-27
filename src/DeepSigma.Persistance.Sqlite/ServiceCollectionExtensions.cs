using DeepSigma.Persistence.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DeepSigma.Persistence.Sqlite;

public static class ServiceCollectionExtensions
{
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
