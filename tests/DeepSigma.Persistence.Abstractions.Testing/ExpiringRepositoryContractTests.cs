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

    private Task WaitForExpiry() => Task.Delay(ShortTtl + ExpiryBuffer);

    // ── Expiry via SetOptions.Ttl ─────────────────────────────────────────

    [Fact]
    public async Task GetAsync_AfterTtlExpires_ReturnsNull()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v", new SetOptions { Ttl = ShortTtl });
        await WaitForExpiry();
        Assert.Null(await repo.GetAsync("k"));
    }

    [Fact]
    public async Task GetAsync_BeforeTtlExpires_ReturnsValue()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v", new SetOptions { Ttl = TimeSpan.FromSeconds(60) });
        Assert.Equal("v", await repo.GetAsync("k"));
    }

    // ── Expiry via SetOptions.AbsoluteExpiration ──────────────────────────

    [Fact]
    public async Task GetAsync_AfterAbsoluteExpirationPasses_ReturnsNull()
    {
        var repo = CreateRepository();
        var expiry = DateTimeOffset.UtcNow.Add(ShortTtl);
        await repo.SetAsync("k", "v", new SetOptions { AbsoluteExpiration = expiry });
        await WaitForExpiry();
        Assert.Null(await repo.GetAsync("k"));
    }

    // ── Exists / List filter expired ─────────────────────────────────────

    [Fact]
    public async Task ExistsAsync_AfterTtlExpires_ReturnsFalse()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v", new SetOptions { Ttl = ShortTtl });
        await WaitForExpiry();
        Assert.False(await repo.ExistsAsync("k"));
    }

    [Fact]
    public async Task ListKeysAsync_ExcludesExpiredKeys()
    {
        var repo = CreateRepository();
        await repo.SetAsync("live", "v");
        await repo.SetAsync("expiring", "v", new SetOptions { Ttl = ShortTtl });
        await WaitForExpiry();
        var keys = await Collect(repo.ListKeysAsync());
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
        await repo.SetAsync("k", "v", new SetOptions { Ttl = ShortTtl });
        await WaitForExpiry();
        Assert.False(await repo.DeleteAsync("k"));
        Assert.False(await repo.ExistsAsync("k"));   // and stays gone
    }

    // ── GetTtl ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTtlAsync_KeyWithNoExpiration_ReturnsNull()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v");
        Assert.Null(await repo.GetTtlAsync("k"));
    }

    [Fact]
    public async Task GetTtlAsync_MissingKey_ReturnsNull()
    {
        var repo = CreateRepository();
        Assert.Null(await repo.GetTtlAsync("missing"));
    }

    [Fact]
    public async Task GetTtlAsync_KeyWithTtl_ReturnsPositiveRemainder()
    {
        var repo = CreateRepository();
        var ttl = TimeSpan.FromSeconds(30);
        await repo.SetAsync("k", "v", new SetOptions { Ttl = ttl });
        var remaining = await repo.GetTtlAsync("k");
        Assert.NotNull(remaining);
        Assert.True(remaining.Value > TimeSpan.Zero);
        Assert.True(remaining.Value <= ttl);
    }

    // ── SetTtl ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SetTtlAsync_MissingKey_ReturnsFalse()
    {
        var repo = CreateRepository();
        Assert.False(await repo.SetTtlAsync("missing", TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task SetTtlAsync_ExistingKey_AppliesExpiry()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v");
        Assert.True(await repo.SetTtlAsync("k", ShortTtl));
        await WaitForExpiry();
        Assert.Null(await repo.GetAsync("k"));
    }

    [Fact]
    public async Task SetTtlAsync_NullOnKeyWithTtl_RemovesExpiry()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v", new SetOptions { Ttl = ShortTtl });
        Assert.True(await repo.SetTtlAsync("k", null));
        await WaitForExpiry();
        // TTL was removed; key must still be alive
        Assert.Equal("v", await repo.GetAsync("k"));
    }

    [Fact]
    public async Task SetTtlAsync_NullOnKeyWithNoTtl_ReturnsTrueAndKeyLives()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v");
        Assert.True(await repo.SetTtlAsync("k", null));
        Assert.Equal("v", await repo.GetAsync("k"));
        Assert.Null(await repo.GetTtlAsync("k"));
    }

    // ── PurgeExpired ─────────────────────────────────────────────────────

    [Fact]
    public async Task PurgeExpiredAsync_NoExpiredKeys_ReturnsZero()
    {
        var repo = CreateRepository();
        await repo.SetAsync("k", "v");
        Assert.Equal(0, await repo.PurgeExpiredAsync());
    }

    [Fact]
    public async Task PurgeExpiredAsync_RemovesExpiredKeysAndReturnsCount()
    {
        var repo = CreateRepository();
        await repo.SetAsync("live", "v");
        await repo.SetAsync("dead1", "v", new SetOptions { Ttl = ShortTtl });
        await repo.SetAsync("dead2", "v", new SetOptions { Ttl = ShortTtl });
        await WaitForExpiry();
        var purged = await repo.PurgeExpiredAsync();
        Assert.Equal(2, purged);
        // Verify they are gone
        Assert.Null(await repo.GetAsync("dead1"));
        Assert.Null(await repo.GetAsync("dead2"));
        // Verify live key is unaffected
        Assert.Equal("v", await repo.GetAsync("live"));
    }

    [Fact]
    public async Task PurgeExpiredAsync_DoesNotRemoveLiveKeys()
    {
        var repo = CreateRepository();
        await repo.SetAsync("live1", "v");
        await repo.SetAsync("live2", "v", new SetOptions { Ttl = TimeSpan.FromSeconds(60) });
        await repo.PurgeExpiredAsync();
        Assert.Equal("v", await repo.GetAsync("live1"));
        Assert.Equal("v", await repo.GetAsync("live2"));
    }
}
