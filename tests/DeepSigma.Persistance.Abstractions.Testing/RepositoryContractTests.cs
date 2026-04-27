using Xunit;

namespace DeepSigma.Persistence.Testing;

/// <summary>
/// Abstract contract suite for <see cref="IRepository{TValue}"/>.
/// Derive this class in a backend test project and implement <see cref="CreateRepository"/>.
/// </summary>
public abstract class RepositoryContractTests<TRepository>
    where TRepository : IRepository<string>
{
    protected abstract TRepository CreateRepository();

    /// <summary>Override to match the MaxKeyLength configured on the repository under test.</summary>
    protected virtual int MaxKeyLength => 512;

    protected static async Task<List<T>> Collect<T>(IAsyncEnumerable<T> source, CancellationToken ct = default)
    {
        var list = new List<T>();
        await foreach (var item in source.WithCancellation(ct))
            list.Add(item);
        return list;
    }

    // ── Get ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsNull()
    {
        var repo = CreateRepository();
        Assert.Null(await repo.GetAsync("missing-key"));
    }

    [Fact]
    public async Task GetAsync_AfterSet_ReturnsValue()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "hello");
        Assert.Equal("hello", await repo.GetAsync("k"));
    }

    [Fact]
    public async Task GetAsync_AfterOverwrite_ReturnsNewValue()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "first");
        await repo.SetAsync("k", "second");
        Assert.Equal("second", await repo.GetAsync("k"));
    }

    // ── Delete ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingKey_ReturnsTrue()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v");
        Assert.True(await repo.DeleteAsync("k"));
    }

    [Fact]
    public async Task DeleteAsync_ExistingKey_RemovesKey()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v");
        await repo.DeleteAsync("k");
        Assert.Null(await repo.GetAsync("k"));
        Assert.False(await repo.ExistsAsync("k"));
    }

    [Fact]
    public async Task DeleteAsync_MissingKey_ReturnsFalse()
    {
        var repo = CreateRepository();
        Assert.False(await repo.DeleteAsync("missing-key"));
    }

    // ── Exists ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExistsAsync_AfterSet_ReturnsTrue()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v");
        Assert.True(await repo.ExistsAsync("k"));
    }

    [Fact]
    public async Task ExistsAsync_MissingKey_ReturnsFalse()
    {
        var repo = CreateRepository();
        Assert.False(await repo.ExistsAsync("missing-key"));
    }

    [Fact]
    public async Task ExistsAsync_AfterDelete_ReturnsFalse()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v");
        await repo.DeleteAsync("k");
        Assert.False(await repo.ExistsAsync("k"));
    }

    // ── ListKeys ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ListKeysAsync_EmptyStore_ReturnsEmpty()
    {
        var repo = CreateRepository();
        var keys = await Collect(repo.ListKeysAsync());
        Assert.Empty(keys);
    }

    [Fact]
    public async Task ListKeysAsync_MultipleKeys_ReturnsAll()
    {
        var repo = CreateRepository();
        await repo.SetAsync("a", "1");
        await repo.SetAsync("b", "2");
        await repo.SetAsync("c", "3");
        var keys = await Collect(repo.ListKeysAsync());
        Assert.Equal(3, keys.Count);
        Assert.Contains("a", keys);
        Assert.Contains("b", keys);
        Assert.Contains("c", keys);
    }

    [Fact]
    public async Task ListKeysAsync_WithPrefix_ReturnsOnlyMatching()
    {
        var repo = CreateRepository();
        await repo.SetAsync("user:1", "a");
        await repo.SetAsync("user:2", "b");
        await repo.SetAsync("config:x", "c");
        var keys = await Collect(repo.ListKeysAsync("user:"));
        Assert.Equal(2, keys.Count);
        Assert.All(keys, k => Assert.StartsWith("user:", k));
    }

    [Fact]
    public async Task ListKeysAsync_PrefixExactMatch_IncludesKey()
    {
        var repo = CreateRepository();
        await repo.SetAsync("foo", "v");
        await repo.SetAsync("foobar", "v");
        var keys = await Collect(repo.ListKeysAsync("foo"));
        Assert.Contains("foo", keys);
        Assert.Contains("foobar", keys);
    }

    [Fact]
    public async Task ListKeysAsync_PrefixNoMatch_ReturnsEmpty()
    {
        var repo = CreateRepository();
        await repo.SetAsync("a", "1");
        var keys = await Collect(repo.ListKeysAsync("z:"));
        Assert.Empty(keys);
    }

    [Fact]
    public async Task ListKeysAsync_DoesNotIncludeDeletedKeys()
    {
        var repo = CreateRepository();
        await repo.SetAsync("keep", "v");
        await repo.SetAsync("gone", "v");
        await repo.DeleteAsync("gone");
        var keys = await Collect(repo.ListKeysAsync());
        Assert.Contains("keep", keys);
        Assert.DoesNotContain("gone", keys);
    }

    // ── List ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_MultipleKeys_ReturnsAllPairs()
    {
        var repo = CreateRepository();
        await repo.SetAsync("x", "val-x");
        await repo.SetAsync("y", "val-y");
        var pairs = await Collect(repo.ListAsync());
        Assert.Equal(2, pairs.Count);
        Assert.Contains(pairs, p => p.Key == "x" && p.Value == "val-x");
        Assert.Contains(pairs, p => p.Key == "y" && p.Value == "val-y");
    }

    [Fact]
    public async Task ListAsync_WithPrefix_ReturnsOnlyMatching()
    {
        var repo = CreateRepository();
        await repo.SetAsync("ns:a", "1");
        await repo.SetAsync("ns:b", "2");
        await repo.SetAsync("other", "3");
        var pairs = await Collect(repo.ListAsync("ns:"));
        Assert.Equal(2, pairs.Count);
        Assert.All(pairs, p => Assert.StartsWith("ns:", p.Key));
    }

    // ── Key validation ───────────────────────────────────────────────────

    [Fact]
    public async Task SetAsync_NullKey_Throws()
    {
        var repo = CreateRepository();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => repo.SetAsync(null!, "v"));
    }

    [Fact]
    public async Task SetAsync_EmptyKey_Throws()
    {
        var repo = CreateRepository();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => repo.SetAsync("", "v"));
    }

    [Fact]
    public async Task SetAsync_NullByteKey_Throws()
    {
        var repo = CreateRepository();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => repo.SetAsync("key\0bad", "v"));
    }

    [Fact]
    public async Task SetAsync_KeyAtMaxLength_Succeeds()
    {
        var repo = CreateRepository();
        var key = new string('k', MaxKeyLength);
        await repo.SetAsync(key, "v"); // must not throw
        Assert.Equal("v", await repo.GetAsync(key));
    }

    [Fact]
    public async Task SetAsync_KeyExceedsMaxLength_Throws()
    {
        var repo = CreateRepository();
        var key = new string('k', MaxKeyLength + 1);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => repo.SetAsync(key, "v"));
    }

    // ── Special characters in keys ────────────────────────────────────────

    [Theory]
    [InlineData("key with spaces")]
    [InlineData("key/with/slashes")]
    [InlineData("key.with.dots")]
    [InlineData("key:with:colons")]
    [InlineData("key=with=equals")]
    [InlineData("unicode-кey-值")]
    [InlineData("emoji-🔑-key")]
    public async Task SetAsync_SpecialCharacterKey_RoundTrips(string key)
    {
        var repo = CreateRepository();
        await repo.SetAsync(key, "value");
        Assert.Equal("value", await repo.GetAsync(key));
        Assert.True(await repo.ExistsAsync(key));
    }

    // ── SetOptions validation (abstraction-level, not backend-level) ──────

    [Fact]
    public void SetOptions_BothTtlAndAbsoluteExpiration_Throws()
    {
        Assert.Throws<ArgumentException>(() => new SetOptions
        {
            Ttl = TimeSpan.FromSeconds(10),
            AbsoluteExpiration = DateTimeOffset.UtcNow.AddSeconds(10)
        });
    }
}
