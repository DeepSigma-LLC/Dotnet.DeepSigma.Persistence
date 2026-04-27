using DeepSigma.Persistence.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DeepSigma.Persistence.FileSystem;

/// <summary>
/// Extension methods for registering FileSystem persistence services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds file system-based persistence services to the specified dependency injection container.
    /// </summary>
    /// <remarks>Registers a generic repository implementation and, if background purging is enabled in the
    /// options, adds a hosted service for automatic file cleanup. This method should be called during application
    /// startup.</remarks>
    /// <param name="services">The service collection to which the file system persistence services will be added.</param>
    /// <param name="configure">A delegate to configure the file system persistence options before registration. Cannot be null.</param>
    /// <returns>The same service collection instance so that additional calls can be chained.</returns>
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
