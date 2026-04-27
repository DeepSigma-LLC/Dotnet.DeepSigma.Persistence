using DeepSigma.Persistence.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DeepSigma.Persistence.InMemory;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the InMemory persistence backend.
    /// <para>
    /// <see cref="InMemoryStore{TValue}"/> is registered as a singleton per TValue so that
    /// <see cref="IRepository{TValue}"/>, <see cref="IBulkRepository{TValue}"/>, and
    /// <see cref="IExpiringRepository{TValue}"/> all operate on the same underlying data.
    /// </para>
    /// </summary>
    public static IServiceCollection AddInMemoryPersistence(
        this IServiceCollection services,
        Action<InMemoryOptions>? configure = null)
    {
        var options = new InMemoryOptions();
        configure?.Invoke(options);
        options.Validate();
        services.TryAddSingleton(options);

        // Shared store — one ConcurrentDictionary per TValue, regardless of how many interface
        // registrations resolve to InMemoryRepository<TValue>.
        services.AddSingleton(typeof(InMemoryStore<>));

        services.AddRepositoryImplementation(typeof(InMemoryRepository<>));

        return services;
    }
}
