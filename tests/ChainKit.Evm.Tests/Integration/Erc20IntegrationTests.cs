using Xunit;

namespace ChainKit.Evm.Tests.Integration;

/// <summary>
/// Integration tests for ERC-20 operations on a local Anvil node.
/// Without deploying a real contract, these tests verify the contract factory
/// and that calling methods on non-existent contracts returns appropriate results.
/// </summary>
[Trait("Category", "Integration")]
public class Erc20IntegrationTests : IClassFixture<AnvilFixture>
{
    private readonly AnvilFixture _anvil;
    private readonly EvmClient _client;

    public Erc20IntegrationTests(AnvilFixture anvil)
    {
        _anvil = anvil;
        _client = new EvmClient(_anvil.Provider, _anvil.Network);
    }

    [Fact]
    public void GetErc20Contract_ReturnsInstance()
    {
        var contract = _client.GetErc20Contract("0xdAC17F958D2ee523a2206206994597C13D831ec7");

        Assert.NotNull(contract);
        Assert.Equal("0xdAC17F958D2ee523a2206206994597C13D831ec7", contract.ContractAddress);
    }

    [Fact]
    public async Task Erc20_NameOnNonContract_ReturnsFailOrEmpty()
    {
        // Calling name() on a precompile address (no ERC-20 contract deployed)
        // should either fail or return empty string — both are acceptable.
        var contract = _client.GetErc20Contract("0x0000000000000000000000000000000000000001");
        var result = await contract.NameAsync();

        // On Anvil, calling a non-contract address returns empty data
        // which should either fail or return empty string — either is acceptable
        Assert.True(!result.Success || string.IsNullOrEmpty(result.Data));
    }

    [Fact]
    public async Task Erc20_BalanceOfOnNonContract_ReturnsFailOrZero()
    {
        // Querying balanceOf on a non-contract address
        var contract = _client.GetErc20Contract("0x0000000000000000000000000000000000000001");
        var result = await contract.BalanceOfAsync(_anvil.Account0.Address);

        // Should either fail or return zero balance
        Assert.True(!result.Success || result.Data == System.Numerics.BigInteger.Zero);
    }
}
