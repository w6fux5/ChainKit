using ChainKit.Evm.Providers;
using Xunit;

namespace ChainKit.Evm.Tests.E2E;

[Trait("Category", "E2E")]
public class SepoliaTests
{
    private readonly EvmClient _client;

    public SepoliaTests()
    {
        var provider = new EvmHttpProvider(EvmNetwork.Sepolia);
        _client = new EvmClient(provider, EvmNetwork.Sepolia);
    }

    [Fact]
    public async Task GetBlockNumber_ReturnsPositive()
    {
        var result = await _client.GetBlockNumberAsync();
        Assert.True(result.Success, result.Error?.Message ?? "");
        Assert.True(result.Data > 0);
    }

    [Fact]
    public async Task GetBalance_ZeroAddress_ReturnsResult()
    {
        var result = await _client.GetBalanceAsync("0x0000000000000000000000000000000000000000");
        Assert.True(result.Success);
    }
}
