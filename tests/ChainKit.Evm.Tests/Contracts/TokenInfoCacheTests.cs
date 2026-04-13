using ChainKit.Evm.Contracts;
using ChainKit.Evm.Models;
using Xunit;

namespace ChainKit.Evm.Tests.Contracts;

public class TokenInfoCacheTests
{
    [Fact]
    public async Task GetOrResolveAsync_KnownToken_DoesNotCallResolver()
    {
        var cache = new TokenInfoCache();
        var resolverCalled = false;
        var info = await cache.GetOrResolveAsync(
            "0xdAC17F958D2ee523a2206206994597C13D831ec7", 1,
            _ => { resolverCalled = true; return Task.FromResult<TokenInfo?>(null); });
        Assert.False(resolverCalled);
        Assert.NotNull(info);
        Assert.Equal("USDT", info!.Symbol);
    }

    [Fact]
    public async Task GetOrResolveAsync_UnknownToken_CallsResolverAndCaches()
    {
        var cache = new TokenInfoCache();
        var callCount = 0;
        var tokenInfo = new TokenInfo("0xaaa", "Test Token", "TST", 18, default, null);

        var info = await cache.GetOrResolveAsync("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 1,
            _ => { callCount++; return Task.FromResult<TokenInfo?>(tokenInfo); });
        Assert.Equal(1, callCount);
        Assert.Equal("TST", info!.Symbol);

        // Second call should use cache
        await cache.GetOrResolveAsync("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 1,
            _ => { callCount++; return Task.FromResult<TokenInfo?>(null); });
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetOrResolveAsync_ResolverThrows_ReturnsNull()
    {
        var cache = new TokenInfoCache();
        var info = await cache.GetOrResolveAsync("0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 1,
            _ => throw new HttpRequestException("timeout"));
        Assert.Null(info);
    }

    [Fact]
    public async Task GetOrResolveAsync_DifferentChainId_DifferentEntry()
    {
        var cache = new TokenInfoCache();
        var callCount = 0;
        var tokenInfo = new TokenInfo("0xccc", "Token", "TKN", 18, default, null);

        await cache.GetOrResolveAsync("0xcccccccccccccccccccccccccccccccccccccccc", 1,
            _ => { callCount++; return Task.FromResult<TokenInfo?>(tokenInfo); });
        await cache.GetOrResolveAsync("0xcccccccccccccccccccccccccccccccccccccccc", 137,
            _ => { callCount++; return Task.FromResult<TokenInfo?>(tokenInfo); });
        Assert.Equal(2, callCount); // Different chains = different entries
    }
}
