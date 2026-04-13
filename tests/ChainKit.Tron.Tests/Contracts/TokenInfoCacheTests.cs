using System.Numerics;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ChainKit.Core.Crypto;
using ChainKit.Tron.Contracts;
using ChainKit.Tron.Crypto;
using ChainKit.Tron.Providers;
using Xunit;

namespace ChainKit.Tron.Tests.Contracts;

public class TokenInfoCacheTests
{
    private readonly ITronProvider _provider = Substitute.For<ITronProvider>();

    // Known USDT mainnet hex address
    private const string UsdtHex = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";

    // An address not in the known-tokens table
    private const string UnknownContract = "41aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Get_KnownToken_ReturnsImmediately()
    {
        var cache = new TokenInfoCache();
        var info = cache.Get(UsdtHex);

        Assert.NotNull(info);
        Assert.Equal("USDT", info!.Symbol);
        Assert.Equal(6, info.Decimals);
    }

    [Fact]
    public void Get_KnownToken_CaseInsensitive()
    {
        var cache = new TokenInfoCache();
        var info = cache.Get(UsdtHex.ToUpperInvariant());

        Assert.NotNull(info);
        Assert.Equal("USDT", info!.Symbol);
    }

    [Fact]
    public void Get_UnknownToken_ReturnsNull()
    {
        var cache = new TokenInfoCache();
        var info = cache.Get(UnknownContract);

        Assert.Null(info);
    }

    [Fact]
    public void Set_ManuallyAddsToCache()
    {
        var cache = new TokenInfoCache();
        cache.Set(UnknownContract, new TokenInfo("TEST", 8));

        var info = cache.Get(UnknownContract);
        Assert.NotNull(info);
        Assert.Equal("TEST", info!.Symbol);
        Assert.Equal(8, info.Decimals);
    }

    [Fact]
    public async Task GetOrResolveAsync_KnownToken_DoesNotCallProvider()
    {
        var cache = new TokenInfoCache();
        var info = await cache.GetOrResolveAsync(UsdtHex, _provider);

        Assert.Equal("USDT", info.Symbol);
        Assert.Equal(6, info.Decimals);

        // Provider should not have been called
        await _provider.DidNotReceive().TriggerConstantContractAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrResolveAsync_UnknownToken_CallsContractAndCaches()
    {
        // Arrange: mock symbol() to return "MYTKN"
        var symbolBytes = BuildAbiString("MYTKN");
        _provider.TriggerConstantContractAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is<string>(s => s == "symbol()"),
                Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(symbolBytes);

        // Arrange: mock decimals() to return 18
        var decimalsBytes = AbiEncoder.EncodeUint256(new BigInteger(18));
        _provider.TriggerConstantContractAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is<string>(s => s == "decimals()"),
                Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(decimalsBytes);

        var cache = new TokenInfoCache();

        // Act
        var info = await cache.GetOrResolveAsync(UnknownContract, _provider);

        Assert.Equal("MYTKN", info.Symbol);
        Assert.Equal(18, info.Decimals);

        // Verify cached — second call should NOT hit provider again
        _provider.ClearReceivedCalls();
        var info2 = await cache.GetOrResolveAsync(UnknownContract, _provider);

        Assert.Equal("MYTKN", info2.Symbol);
        Assert.Equal(18, info2.Decimals);

        await _provider.DidNotReceive().TriggerConstantContractAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrResolveAsync_ContractCallFails_ReturnsEmptySymbolAndZeroDecimals()
    {
        _provider.TriggerConstantContractAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("timeout"));

        var cache = new TokenInfoCache();
        var info = await cache.GetOrResolveAsync(UnknownContract, _provider);

        Assert.Equal("", info.Symbol);
        Assert.Equal(0, info.Decimals);
    }

    [Fact]
    public void NormalizeAddress_HexAddress_LowercasesIt()
    {
        var upper = "41AABBCCDD00112233445566778899AABBCCDDEEFF";
        var result = TokenInfoCache.NormalizeAddress(upper);
        Assert.Equal(upper.ToLowerInvariant(), result);
    }

    /// <summary>
    /// Builds an ABI-encoded string return value (offset + length + data padded to 32 bytes).
    /// </summary>
    private static byte[] BuildAbiString(string value)
    {
        var strBytes = System.Text.Encoding.UTF8.GetBytes(value);
        // ABI: offset (32 bytes, pointing to 0x20) + length (32 bytes) + data (padded to 32-byte boundary)
        var paddedLen = ((strBytes.Length + 31) / 32) * 32;
        var result = new byte[32 + 32 + paddedLen];
        // Offset = 0x20 = 32
        result[31] = 0x20;
        // Length
        var lenBytes = AbiEncoder.EncodeUint256(new BigInteger(strBytes.Length));
        Buffer.BlockCopy(lenBytes, 0, result, 32, 32);
        // String data
        Buffer.BlockCopy(strBytes, 0, result, 64, strBytes.Length);
        return result;
    }
}
