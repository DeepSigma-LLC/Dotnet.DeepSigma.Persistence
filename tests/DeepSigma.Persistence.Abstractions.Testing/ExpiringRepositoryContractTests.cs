using Xunit;

namespace DeepSigma.Persistence.Testing;

/// <summary>
/// Contract suite for <see cref="IExpiringRepository{TValue}"/>.
/// Inherits all bulk and base contract cases.
/// </summary>
public abstract class ExpiringRepositoryContractTests<TRepository>
    : BulkRepositoryContractTests<TRepository>
    where TRepository : IExpiringRepository<string>
{
    /// <summary>
    /// TTL used for expiry tests. Set high enough to survive a contended CI runner —
    /// the prior 60ms value passed locally but went red intermittently under load.
    /// </summary>
    protected virtual TimeSpan ShortTtl => TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Extra wait added on top of <see cref="ShortTtl"/> to let expiry settle before assertions.
    /// Override (e.g. to 1500ms) for backends with high per-call overhead like Testcontainers.
    /// </summary>
    protected virtual TimeSpan ExpiryBuffer => TimeSpan.FromMilliseconds(500);

    private Task WaitForExpiry() => Task.Delay(ShortTtl + ExpiryBuffer, TestContext.Current.CancellationToken);

    // ── Expiry via SetOptions.Ttl ─────────────────────────────────────────

    [Fact]
    public async Task GetAsync_AfterTtlExpires_ReturnsNull()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v", new SetOptions { Ttl = ShortTtl }, ct: TestContext.Current.CancellationToken);
        await WaitForExpiry();
        Assert.Null(await repo.GetAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAsync_BeforeTtlExpires_ReturnsValue()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v", new SetOptions { Ttl = TimeSpan.FromSeconds(60) }, ct: TestContext.Current.CancellationToken);
        Assert.Equal("v", await repo.GetAsync("k", TestContext.Current.CancellationToken));
    }

    // ── Expiry via SetOptions.AbsoluteExpiration ──────────────────────────

    [Fact]
    public async Task GetAsync_AfterAbsoluteExpirationPasses_ReturnsNull()
    {
        var repo = CreateRepository();
        var expiry = DateTimeOffset.UtcNow.Add(ShortTtl);
        await repo.SetAsync("k", "v", new SetOptions { AbsoluteExpiration = expiry }, ct: TestContext.Current.CancellationToken);
        await WaitForExpiry();
        Assert.Null(await repo.GetAsync("k", TestContext.Current.CancellationToken));
    }

    // ── Exists / List filter expired ─────────────────────────────────────

    [Fact]
    public async Task ExistsAsync_AfterTtlExpires_ReturnsFalse()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v", new SetOptions { Ttl = ShortTtl }, ct: TestContext.Current.CancellationToken);
        await WaitForExpiry();
        Assert.False(await repo.ExistsAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListKeysAsync_ExcludesExpiredKeys()
    {
        var repo = CreateRepository();
        await repo.SetAsync("live", "v", ct: TestContext.Current.CancellationToken);
        await repo.SetAsync("expiring", "v", new SetOptions { Ttl = ShortTtl }, ct: TestContext.Current.CancellationToken);
        await WaitForExpiry();
        var keys = await Collect(repo.ListKeysAsync(ct: TestContext.Current.CancellationToken), ct: TestContext.Current.CancellationToken);
        Assert.Contains("live", keys);
        Assert.DoesNotContain("expiring", keys);
    }

    [Fact]
    public async Task DeleteAsync_OnExpiredKey_ReturnsFalse()
    {
        // Pins down the boolean return contract across backends. Some backends opportunistically
        // remove the storage entry on this call as a side effect; that's an implementation detail.
        // What we guarantee is: deleting an already-expired key reports false.
        var repo = CreateRepository();
        await repo.SetAsync("k", "v", new SetOptions { Ttl = ShortTtl }, ct: TestContext.Current.CancellationToken);
        await WaitForExpiry();
        Assert.False(await repo.DeleteAsync("k", TestContext.Current.CancellationToken));
        Assert.False(await repo.ExistsAsync("k", TestContext.Current.CancellationToken));   // and stays gone
    }

    // ── GetTtl ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTtlAsync_KeyWithNoExpiration_ReturnsNull()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v", ct: TestContext.Current.CancellationToken);
        Assert.Null(await repo.GetTtlAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetTtlAsync_MissingKey_ReturnsNull()
    {
        var repo = CreateRepository();
        Assert.Null(await repo.GetTtlAsync("missing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetTtlAsync_KeyWithTtl_ReturnsPositiveRemainder()
    {
        var repo = CreateRepository();
        var ttl = TimeSpan.FromSeconds(30);
        await repo.SetAsync("k", "v", new SetOptions { Ttl = ttl }, ct: TestContext.Current.CancellationToken);
        var remaining = await repo.GetTtlAsync("k", TestContext.Current.CancellationToken);
        Assert.NotNull(remaining);
        Assert.True(remaining.Value > TimeSpan.Zero);
        Assert.True(remaining.Value <= ttl);
    }

    // ── SetTtl ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SetTtlAsync_MissingKey_ReturnsFalse()
    {
        var repo = CreateRepository();
        Assert.False(await repo.SetTtlAsync("missing", TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetTtlAsync_ExistingKey_AppliesExpiry()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v", ct: TestContext.Current.CancellationToken);
        Assert.True(await repo.SetTtlAsync("k", ShortTtl, TestContext.Current.CancellationToken));
        await WaitForExpiry();
        Assert.Null(await repo.GetAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetTtlAsync_NullOnKeyWithTtl_RemovesExpiry()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v", new SetOptions { Ttl = ShortTtl }, ct: TestContext.Current.CancellationToken);
        Assert.True(await repo.SetTtlAsync("k", null, TestContext.Current.CancellationToken));
        await WaitForExpiry();
        // TTL was removed; key must still be alive
        Assert.Equal("v", await repo.GetAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetTtlAsync_NullOnKeyWithNoTtl_ReturnsTrueAndKeyLives()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v", ct: TestContext.Current.CancellationToken);
        Assert.True(await repo.SetTtlAsync("k", null, TestContext.Current.CancellationToken));
        Assert.Equal("v", await repo.GetAsync("k", TestContext.Current.CancellationToken));
        Assert.Null(await repo.GetTtlAsync("k", TestContext.Current.CancellationToken));
    }

    // ── PurgeExpired ─────────────────────────────────────────────────────

    [Fact]
    public async Task PurgeExpiredAsync_NoExpiredKeys_ReturnsZero()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v", ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await repo.PurgeExpiredAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PurgeExpiredAsync_RemovesExpiredKeysAndReturnsCount()
    {
        var repo = CreateRepository();
        await repo.SetAsync("live", "v", ct: TestContext.Current.CancellationToken);
        await repo.SetAsync("dead1", "v", new SetOptions { Ttl = ShortTtl }, ct: TestContext.Current.CancellationToken);
        await repo.SetAsync("dead2", "v", new SetOptions { Ttl = ShortTtl }, ct: TestContext.Current.CancellationToken);
        await WaitForExpiry();
        var purged = await repo.PurgeExpiredAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, purged);
        // Verify they are gone
        Assert.Null(await repo.GetAsync("dead1", TestContext.Current.CancellationToken));
        Assert.Null(await repo.GetAsync("dead2", TestContext.Current.CancellationToken));
        // Verify live key is unaffected
        Assert.Equal("v", await repo.GetAsync("live", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PurgeExpiredAsync_DoesNotRemoveLiveKeys()
    {
        var repo = CreateRepository();
        await repo.SetAsync("live1", "v", ct: TestContext.Current.CancellationToken);
        await repo.SetAsync("live2", "v", new SetOptions { Ttl = TimeSpan.FromSeconds(60) }, ct: TestContext.Current.CancellationToken);
        await repo.PurgeExpiredAsync(TestContext.Current.CancellationToken);
        Assert.Equal("v", await repo.GetAsync("live1", TestContext.Current.CancellationToken));
        Assert.Equal("v", await repo.GetAsync("live2", TestContext.Current.CancellationToken));
    }

    // ── Concurrency ──────────────────────────────────────────────────────

    [Fact]
    public async Task SetTtlAsync_ConcurrentOnSameKey_AllReturnTrueAndValuePreserved()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v", ct: TestContext.Current.CancellationToken);

        // Each task races to install a different TTL on the same key. The contract: every call
        // succeeds (the key is alive throughout), no call throws, and the underlying value is
        // not lost — only the expiry changes.
        var setOps = Enumerable.Range(0, ConcurrencyDegree)
            .Select(i => Task.Run(() => repo.SetTtlAsync("k", TimeSpan.FromSeconds(60 + i), TestContext.Current.CancellationToken)))
            .ToArray();
        var results = await Task.WhenAll(setOps);

        Assert.All(results, Assert.True);
        Assert.Equal("v", await repo.GetAsync("k", TestContext.Current.CancellationToken));
    }
}
