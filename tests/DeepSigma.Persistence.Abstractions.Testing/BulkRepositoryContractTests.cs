using Xunit;

namespace DeepSigma.Persistence.Testing;

/// <summary>
/// Contract suite for <see cref="IBulkRepository{TValue}"/>.
/// Inherits all base <see cref="RepositoryContractTests{TRepository}"/> cases.
/// </summary>
public abstract class BulkRepositoryContractTests<TRepository>
    : RepositoryContractTests<TRepository>
    where TRepository : IBulkRepository<string>
{
    // ── GetMany ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetManyAsync_EmptyInput_ReturnsEmptyDictionary()
    {
        var repo = CreateRepository();
        var result = await repo.GetManyAsync([]);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetManyAsync_AllPresent_ReturnsAll()
    {
        var repo = CreateRepository();
        await repo.SetAsync("a", "1");
        await repo.SetAsync("b", "2");
        var result = await repo.GetManyAsync(["a", "b"]);
        Assert.Equal(2, result.Count);
        Assert.Equal("1", result["a"]);
        Assert.Equal("2", result["b"]);
    }

    [Fact]
    public async Task GetManyAsync_SomePresent_ReturnsOnlyFound()
    {
        var repo = CreateRepository();
        await repo.SetAsync("present", "v");
        var result = await repo.GetManyAsync(["present", "missing"]);
        Assert.Single(result);
        Assert.True(result.ContainsKey("present"));
        Assert.False(result.ContainsKey("missing"));
    }

    [Fact]
    public async Task GetManyAsync_NonePresent_ReturnsEmptyDictionary()
    {
        var repo = CreateRepository();
        var result = await repo.GetManyAsync(["x", "y", "z"]);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetManyAsync_NeverContainsNullValues()
    {
        var repo = CreateRepository();
        await repo.SetAsync("a", "v");
        var result = await repo.GetManyAsync(["a", "missing"]);
        Assert.All(result.Values, v => Assert.NotNull(v));
    }

    // ── SetMany ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SetManyAsync_EmptyInput_Succeeds()
    {
        var repo = CreateRepository();
        await repo.SetManyAsync([]); // must not throw
    }

    [Fact]
    public async Task SetManyAsync_MultiplePairs_AllStoredAndRetrievable()
    {
        var repo = CreateRepository();
        var items = new Dictionary<string, string>
        {
            ["one"] = "1",
            ["two"] = "2",
            ["three"] = "3"
        };
        await repo.SetManyAsync(items);
        var result = await repo.GetManyAsync(["one", "two", "three"]);
        Assert.Equal(3, result.Count);
        Assert.Equal("1", result["one"]);
        Assert.Equal("2", result["two"]);
        Assert.Equal("3", result["three"]);
    }

    [Fact]
    public async Task SetManyAsync_OverwritesExistingKeys()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "old");
        await repo.SetManyAsync([new KeyValuePair<string, string>("k", "new")]);
        Assert.Equal("new", await repo.GetAsync("k"));
    }

    // ── DeleteMany ───────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteManyAsync_EmptyInput_ReturnsZero()
    {
        var repo = CreateRepository();
        var count = await repo.DeleteManyAsync([]);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task DeleteManyAsync_AllPresent_ReturnsCount()
    {
        var repo = CreateRepository();
        await repo.SetAsync("a", "1");
        await repo.SetAsync("b", "2");
        var count = await repo.DeleteManyAsync(["a", "b"]);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task DeleteManyAsync_SomePresent_ReturnsCountActuallyDeleted()
    {
        var repo = CreateRepository();
        await repo.SetAsync("present", "v");
        var count = await repo.DeleteManyAsync(["present", "missing"]);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task DeleteManyAsync_NonePresent_ReturnsZero()
    {
        var repo = CreateRepository();
        var count = await repo.DeleteManyAsync(["x", "y"]);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task DeleteManyAsync_RemovesKeys()
    {
        var repo = CreateRepository();
        await repo.SetAsync("a", "1");
        await repo.SetAsync("b", "2");
        await repo.SetAsync("c", "3");
        await repo.DeleteManyAsync(["a", "b"]);
        Assert.Null(await repo.GetAsync("a"));
        Assert.Null(await repo.GetAsync("b"));
        Assert.Equal("3", await repo.GetAsync("c"));
    }
}
