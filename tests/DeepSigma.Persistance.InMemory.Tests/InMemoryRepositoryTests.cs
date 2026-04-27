using DeepSigma.Persistance.InMemory;
using DeepSigma.Persistance.Testing;

namespace DeepSigma.Persistance.InMemory.Tests;

/// <summary>
/// Runs the full contract suite against InMemoryRepository.
/// Each test gets a fresh instance (and therefore a fresh store) via CreateRepository().
/// </summary>
public sealed class InMemoryRepositoryTests
    : ExpiringRepositoryContractTests<InMemoryRepository<string>>
{
    protected override InMemoryRepository<string> CreateRepository() => new();
}
