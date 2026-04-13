using ChainKit.Evm.Models;
using Xunit;

namespace ChainKit.Evm.Tests.Integration;

/// <summary>
/// Integration tests for native ETH transfers on a local Anvil node.
/// Anvil auto-mines: each transaction is confirmed instantly (1 block = 1 tx).
/// </summary>
[Trait("Category", "Integration")]
public class TransferIntegrationTests : IClassFixture<AnvilFixture>
{
    private readonly AnvilFixture _anvil;
    private readonly EvmClient _client;

    public TransferIntegrationTests(AnvilFixture anvil)
    {
        _anvil = anvil;
        _client = new EvmClient(_anvil.Provider, _anvil.Network);
    }

    [Fact]
    public async Task Transfer_1Eth_Success()
    {
        var result = await _client.TransferAsync(_anvil.Account0, _anvil.Account1.Address, 1.0m);

        Assert.True(result.Success, result.Error?.Message ?? "");
        Assert.NotNull(result.Data);
        Assert.NotEmpty(result.Data!.TxId);
    }

    [Fact]
    public async Task GetBalance_ReturnsPositive()
    {
        var result = await _client.GetBalanceAsync(_anvil.Account0.Address);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.True(result.Data!.Balance > 0);
    }

    [Fact]
    public async Task GetBlockNumber_ReturnsNonNegative()
    {
        var result = await _client.GetBlockNumberAsync();

        Assert.True(result.Success);
        Assert.True(result.Data >= 0);
    }

    [Fact]
    public async Task Transfer_ThenGetDetail_ShowsConfirmed()
    {
        var transferResult = await _client.TransferAsync(_anvil.Account0, _anvil.Account1.Address, 0.1m);
        Assert.True(transferResult.Success, transferResult.Error?.Message ?? "");

        var detailResult = await _client.GetTransactionDetailAsync(transferResult.Data!.TxId);

        Assert.True(detailResult.Success);
        Assert.NotNull(detailResult.Data);
        Assert.Equal(TransactionStatus.Confirmed, detailResult.Data!.Status);
    }

    [Fact]
    public async Task Transfer_BalanceChanges()
    {
        var before = await _client.GetBalanceAsync(_anvil.Account1.Address);
        Assert.True(before.Success);

        await _client.TransferAsync(_anvil.Account0, _anvil.Account1.Address, 0.5m);

        var after = await _client.GetBalanceAsync(_anvil.Account1.Address);
        Assert.True(after.Success);

        Assert.True(after.Data!.Balance > before.Data!.Balance);
    }
}
