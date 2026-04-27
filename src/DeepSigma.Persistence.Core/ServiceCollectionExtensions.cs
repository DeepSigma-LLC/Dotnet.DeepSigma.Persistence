using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DeepSigma.Persistence.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="JsonValueSerializer"/> as <see cref="IJsonValueSerializer"/> via TryAddSingleton,
    /// so callers can override by registering their own <see cref="IJsonValueSerializer"/> first.
    /// </summary>
    public static IServiceCollection AddPersistenceCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IJsonValueSerializer, JsonValueSerializer>();
        return services;
    }

    /// <summary>
    /// Registers the supplied open-generic repository type as the implementation of
    /// <see cref="IRepository{TValue}"/>, <see cref="IBulkRepository{TValue}"/>, and
    /// <see cref="IExpiringRepository{TValue}"/>. Used by every backend's AddXxxPersistence extension.
    /// </summary>
    public static IServiceCollection AddRepositoryImplementation(
        this IServiceCollection services,
        Type openGenericRepositoryType)
    {
        if (!openGenericRepositoryType.IsGenericTypeDefinition)
            throw new ArgumentException(
                $"Expected an open generic type (e.g. typeof(MyRepository<>)). Got: {openGenericRepositoryType}.",
                nameof(openGenericRepositoryType));

        services.AddSingleton(typeof(IRepository<>), openGenericRepositoryType);
        services.AddSingleton(typeof(IBulkRepository<>), openGenericRepositoryType);
        services.AddSingleton(typeof(IExpiringRepository<>), openGenericRepositoryType);
        return services;
    }
}
