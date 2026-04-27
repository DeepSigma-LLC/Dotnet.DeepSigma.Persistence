using DeepSigma.Persistence.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DeepSigma.Persistence.FileSystem;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFileSystemPersistence(
        this IServiceCollection services,
        Action<FileSystemOptions> configure)
    {
        var options = new FileSystemOptions { RootPath = "./data" };
        configure(options);
        options.Validate();
        services.TryAddSingleton(options);

        services.AddRepositoryImplementation(typeof(FileSystemRepository<>));

        if (options.BackgroundPurge)
            services.AddHostedService<FileSystemPurgeService>();

        return services;
    }
}
