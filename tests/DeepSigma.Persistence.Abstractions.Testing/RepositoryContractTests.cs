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
        Assert.Null(await repo.GetAsync("missing-key", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAsync_AfterSet_ReturnsValue()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "hello", ct: TestContext.Current.CancellationToken);
        Assert.Equal("hello", await repo.GetAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAsync_AfterOverwrite_ReturnsNewValue()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "first", ct: TestContext.Current.CancellationToken);
        await repo.SetAsync("k", "second", ct: TestContext.Current.CancellationToken);
        Assert.Equal("second", await repo.GetAsync("k", TestContext.Current.CancellationToken));
    }

    // ── Delete ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingKey_ReturnsTrue()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v", ct: TestContext.Current.CancellationToken);
        Assert.True(await repo.DeleteAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_ExistingKey_RemovesKey()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v", ct: TestContext.Current.CancellationToken);
        await repo.DeleteAsync("k", TestContext.Current.CancellationToken);
        Assert.Null(await repo.GetAsync("k", TestContext.Current.CancellationToken));
        Assert.False(await repo.ExistsAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_MissingKey_ReturnsFalse()
    {
        var repo = CreateRepository();
        Assert.False(await repo.DeleteAsync("missing-key", TestContext.Current.CancellationToken));
    }

    // ── Exists ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExistsAsync_AfterSet_ReturnsTrue()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v", ct: TestContext.Current.CancellationToken);
        Assert.True(await repo.ExistsAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistsAsync_MissingKey_ReturnsFalse()
    {
        var repo = CreateRepository();
        Assert.False(await repo.ExistsAsync("missing-key", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistsAsync_AfterDelete_ReturnsFalse()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v", ct: TestContext.Current.CancellationToken);
        await repo.DeleteAsync("k", TestContext.Current.CancellationToken);
        Assert.False(await repo.ExistsAsync("k", TestContext.Current.CancellationToken));
    }

    // ── ListKeys ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ListKeysAsync_EmptyStore_ReturnsEmpty()
    {
        var repo = CreateRepository();
        var keys = await Collect(repo.ListKeysAsync(ct: TestContext.Current.CancellationToken), ct: TestContext.Current.CancellationToken);
        Assert.Empty(keys);
    }

    [Fact]
    public async Task ListKeysAsync_MultipleKeys_ReturnsAll()
    {
        var repo = CreateRepository();
        await repo.SetAsync("a", "1", ct: TestContext.Current.CancellationToken);
        await repo.SetAsync("b", "2", ct: TestContext.Current.CancellationToken);
        await repo.SetAsync("c", "3", ct: TestContext.Current.CancellationToken);
        var keys = await Collect(repo.ListKeysAsync(ct: TestContext.Current.CancellationToken), ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, keys.Count);
        Assert.Contains("a", keys);
        Assert.Contains("b", keys);
        Assert.Contains("c", keys);
    }

    [Fact]
    public async Task ListKeysAsync_WithPrefix_ReturnsOnlyMatching()
    {
        var repo = CreateRepository();
        await repo.SetAsync("user:1", "a", ct: TestContext.Current.CancellationToken);
        await repo.SetAsync("user:2", "b", ct: TestContext.Current.CancellationToken);
        await repo.SetAsync("config:x", "c", ct: TestContext.Current.CancellationToken);
        var keys = await Collect(repo.ListKeysAsync("user:", ct: TestContext.Current.CancellationToken), ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, keys.Count);
        Assert.All(keys, k => Assert.StartsWith("user:", k));
    }

    [Fact]
    public async Task ListKeysAsync_PrefixExactMatch_IncludesKey()
    {
        var repo = CreateRepository();
        await repo.SetAsync("foo", "v", ct: TestContext.Current.CancellationToken);
        await repo.SetAsync("foobar", "v", ct: TestContext.Current.CancellationToken);
        var keys = await Collect(repo.ListKeysAsync("foo", ct: TestContext.Current.CancellationToken), ct: TestContext.Current.CancellationToken);
        Assert.Contains("foo", keys);
        Assert.Contains("foobar", keys);
    }

    [Fact]
    public async Task ListKeysAsync_PrefixNoMatch_ReturnsEmpty()
    {
        var repo = CreateRepository();
        await repo.SetAsync("a", "1", ct: TestContext.Current.CancellationToken);
        var keys = await Collect(repo.ListKeysAsync("z:", ct: TestContext.Current.CancellationToken), ct: TestContext.Current.CancellationToken);
        Assert.Empty(keys);
    }

    [Fact]
    public async Task ListKeysAsync_DoesNotIncludeDeletedKeys()
    {
        var repo = CreateRepository();
        await repo.SetAsync("keep", "v", ct: TestContext.Current.CancellationToken);
        await repo.SetAsync("gone", "v", ct: TestContext.Current.CancellationToken);
        await repo.DeleteAsync("gone", TestContext.Current.CancellationToken);
        var keys = await Collect(repo.ListKeysAsync(ct: TestContext.Current.CancellationToken), ct: TestContext.Current.CancellationToken);
        Assert.Contains("keep", keys);
        Assert.DoesNotContain("gone", keys);
    }

    // ── List ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_MultipleKeys_ReturnsAllPairs()
    {
        var repo = CreateRepository();
        await repo.SetAsync("x", "val-x", ct: TestContext.Current.CancellationToken);
        await repo.SetAsync("y", "val-y", ct: TestContext.Current.CancellationToken);
        var pairs = await Collect(repo.ListAsync(ct: TestContext.Current.CancellationToken), ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, pairs.Count);
        Assert.Contains(pairs, p => p.Key == "x" && p.Value == "val-x");
        Assert.Contains(pairs, p => p.Key == "y" && p.Value == "val-y");
    }

    [Fact]
    public async Task ListAsync_WithPrefix_ReturnsOnlyMatching()
    {
        var repo = CreateRepository();
        await repo.SetAsync("ns:a", "1", ct: TestContext.Current.CancellationToken);
        await repo.SetAsync("ns:b", "2", ct: TestContext.Current.CancellationToken);
        await repo.SetAsync("other", "3", ct: TestContext.Current.CancellationToken);
        var pairs = await Collect(repo.ListAsync("ns:", ct: TestContext.Current.CancellationToken), ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, pairs.Count);
        Assert.All(pairs, p => Assert.StartsWith("ns:", p.Key));
    }

    // ── Key validation ───────────────────────────────────────────────────

    [Fact]
    public async Task SetAsync_NullKey_Throws()
    {
        var repo = CreateRepository();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => repo.SetAsync(null!, "v", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetAsync_EmptyKey_Throws()
    {
        var repo = CreateRepository();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => repo.SetAsync("", "v", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetAsync_NullByteKey_Throws()
    {
        var repo = CreateRepository();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => repo.SetAsync("key\0bad", "v", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetAsync_KeyAtMaxLength_Succeeds()
    {
        var repo = CreateRepository();
        var key = new string('k', MaxKeyLength);
        await repo.SetAsync(key, "v", ct: TestContext.Current.CancellationToken); // must not throw
        Assert.Equal("v", await repo.GetAsync(key, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetAsync_KeyExceedsMaxLength_Throws()
    {
        var repo = CreateRepository();
        var key = new string('k', MaxKeyLength + 1);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => repo.SetAsync(key, "v", ct: TestContext.Current.CancellationToken));
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
        await repo.SetAsync(key, "value", ct: TestContext.Current.CancellationToken);
        Assert.Equal("value", await repo.GetAsync(key, TestContext.Current.CancellationToken));
        Assert.True(await repo.ExistsAsync(key, TestContext.Current.CancellationToken));
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

    // ── Concurrency ──────────────────────────────────────────────────────
    //
    // Every backend documents thread-safety via a different mechanism (ConcurrentDictionary,
    // AsyncKeyedLocker, row-level locking, WAL + busy_timeout). These tests pin the contract:
    // concurrent operations must not throw, must not lose data, and must converge to one of
    // the written values when racing on a single key.

    /// <summary>Number of concurrent operations used by the concurrency contract tests.
    /// Kept modest so the Postgres testcontainer suite stays under reasonable per-test latency.</summary>
    protected virtual int ConcurrencyDegree => 20;

    [Fact]
    public async Task SetAsync_ConcurrentWritesSameKey_NoExceptionAndValueIsOneOfWritten()
    {
        var repo = CreateRepository();
        var writes = Enumerable.Range(0, ConcurrencyDegree)
            .Select(i => Task.Run(() => repo.SetAsync("k", $"v{i}", ct: TestContext.Current.CancellationToken)))
            .ToArray();
        await Task.WhenAll(writes);

        var final = await repo.GetAsync("k", TestContext.Current.CancellationToken);
        Assert.NotNull(final);
        Assert.Contains(final, Enumerable.Range(0, ConcurrencyDegree).Select(i => $"v{i}"));
    }

    [Fact]
    public async Task SetAsync_ConcurrentWritesDifferentKeys_AllPersisted()
    {
        var repo = CreateRepository();
        var writes = Enumerable.Range(0, ConcurrencyDegree)
            .Select(i => Task.Run(() => repo.SetAsync($"k{i}", $"v{i}", ct: TestContext.Current.CancellationToken)))
            .ToArray();
        await Task.WhenAll(writes);

        for (var i = 0; i < ConcurrencyDegree; i++)
            Assert.Equal($"v{i}", await repo.GetAsync($"k{i}", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_ConcurrentDeleteAndGetSameKey_NoException()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v", ct: TestContext.Current.CancellationToken);
        // Race one delete against many reads. The contract only requires no exception;
        // each Get may see the value or null depending on ordering.
        var reads = Enumerable.Range(0, ConcurrencyDegree)
            .Select(_ => Task.Run(() => repo.GetAsync("k", TestContext.Current.CancellationToken)))
            .ToArray();
        var delete = Task.Run(() => repo.DeleteAsync("k", TestContext.Current.CancellationToken));
        await Task.WhenAll(reads.Concat(new Task[] { delete }));
    }
}
