using System.Numerics;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ChainKit.Tron.Contracts;
using ChainKit.Tron.Models;
using ChainKit.Tron.Providers;
using ChainKit.Tron.Crypto;
using ChainKit.Core.Extensions;
using Xunit;

namespace ChainKit.Tron.Tests;

public class TronClientTests
{
    private readonly ITronProvider _provider = Substitute.For<ITronProvider>();
    private readonly TronClient _client;

    // A deterministic private key for testing
    private static readonly byte[] TestPrivateKey =
        "0000000000000000000000000000000000000000000000000000000000000001".FromHex();

    public TronClientTests()
    {
        _client = new TronClient(_provider);
    }

    [Fact]
    public void Provider_IsExposed()
    {
        Assert.Same(_provider, _client.Provider);
    }

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TronClient(null!));
    }

    // === TransferTrxAsync ===

    [Fact]
    public async Task TransferTrxAsync_Success_ReturnsTxId()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(1000, "0000000000000100abcdef1234567890abcdef1234567890abcdef1234567890",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, new byte[34]));

        _provider.BroadcastTransactionAsync(
                Arg.Any<ChainKit.Tron.Protocol.Protobuf.Transaction>(),
                Arg.Any<CancellationToken>())
            .Returns(new BroadcastResult(true, "txhash123", null));

        var result = await _client.TransferTrxAsync(account, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", 10m);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("txhash123", result.Data!.TxId);
        Assert.Equal(10m, result.Data.Amount);
    }

    [Fact]
    public async Task TransferTrxAsync_BroadcastFails_ReturnsError()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(1000, "0000000000000100abcdef1234567890abcdef1234567890abcdef1234567890",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, new byte[34]));

        _provider.BroadcastTransactionAsync(
                Arg.Any<ChainKit.Tron.Protocol.Protobuf.Transaction>(),
                Arg.Any<CancellationToken>())
            .Returns(new BroadcastResult(false, null, "BANDWIDTH_ERROR"));

        var result = await _client.TransferTrxAsync(account, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", 10m);

        Assert.False(result.Success);
        Assert.Contains("BANDWIDTH", result.Error!.Message);
    }

    [Fact]
    public async Task TransferTrxAsync_ProviderThrows_ReturnsFailResult()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _client.TransferTrxAsync(account, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", 10m);

        Assert.False(result.Success);
        Assert.Contains("Connection refused", result.Error!.Message);
    }

    [Fact]
    public async Task TransferTrxAsync_BroadcastWithNullTxId_UsesComputedTxId()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(1000, "0000000000000100abcdef1234567890abcdef1234567890abcdef1234567890",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, new byte[34]));

        _provider.BroadcastTransactionAsync(
                Arg.Any<ChainKit.Tron.Protocol.Protobuf.Transaction>(),
                Arg.Any<CancellationToken>())
            .Returns(new BroadcastResult(true, null, null));

        var result = await _client.TransferTrxAsync(account, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", 5m);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        // Should have computed a txId even though broadcast returned null
        Assert.NotNull(result.Data!.TxId);
        Assert.NotEmpty(result.Data.TxId);
    }

    // === GetBalanceAsync ===

    [Fact]
    public async Task GetBalanceAsync_ReturnsTrxBalance()
    {
        _provider.GetAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountInfo("41abc", 10_000_000, 0, 0, 0)); // 10 TRX

        var result = await _client.GetBalanceAsync("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t");

        Assert.True(result.Success);
        Assert.Equal(10m, result.Data!.TrxBalance);
        Assert.Empty(result.Data.Trc20Balances);
    }

    [Fact]
    public async Task GetBalanceAsync_ReturnsTrxAndTrc20()
    {
        _provider.GetAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountInfo("41abc", 10_000_000, 0, 0, 0)); // 10 TRX

        _provider.TriggerConstantContractAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new byte[32]); // 0 balance

        var result = await _client.GetBalanceAsync("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", "TRC20ContractAddr");

        Assert.True(result.Success);
        Assert.Equal(10m, result.Data!.TrxBalance);
        Assert.Single(result.Data.Trc20Balances);
        Assert.Equal(0m, result.Data.Trc20Balances["TRC20ContractAddr"]);
    }

    [Fact]
    public async Task GetBalanceAsync_Trc20QueryFails_ReportsZero()
    {
        _provider.GetAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountInfo("41abc", 5_000_000, 0, 0, 0));

        _provider.TriggerConstantContractAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("timeout"));

        var result = await _client.GetBalanceAsync("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", "SomeTRC20");

        Assert.True(result.Success);
        Assert.Equal(5m, result.Data!.TrxBalance);
        Assert.Equal(0m, result.Data.Trc20Balances["SomeTRC20"]);
    }

    [Fact]
    public async Task GetBalanceAsync_ProviderThrows_ReturnsFailResult()
    {
        _provider.GetAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _client.GetBalanceAsync("someaddr");

        Assert.False(result.Success);
        Assert.Contains("Connection refused", result.Error!.Message);
    }

    // === GetTransactionDetailAsync ===

    [Fact]
    public async Task GetTransactionDetailAsync_NotFound_ReturnsFail()
    {
        _provider.GetTransactionByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto("", 0, 0, "", 0, 0, 0));

        var result = await _client.GetTransactionDetailAsync("nonexistent_tx");

        Assert.False(result.Success);
        Assert.Contains("not found", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_Confirmed_ReturnsDetail()
    {
        var txId = "abc123";

        _provider.GetTransactionByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 100, 1700000000000, "SUCCESS", 0, 0, 0));

        _provider.GetTransactionInfoByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 100, 1700000000000, "SUCCESS", 1000, 200, 300));

        var result = await _client.GetTransactionDetailAsync(txId);

        Assert.True(result.Success);
        Assert.Equal(txId, result.Data!.TxId);
        Assert.Equal(TransactionStatus.Confirmed, result.Data.Status);
        Assert.Equal(100, result.Data.BlockNumber);
        Assert.NotNull(result.Data.Cost);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_Unconfirmed_WhenSolidityFails()
    {
        var txId = "abc123";

        _provider.GetTransactionByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 100, 1700000000000, "", 0, 0, 0));

        _provider.GetTransactionInfoByIdAsync(txId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Solidity node unavailable"));

        var result = await _client.GetTransactionDetailAsync(txId);

        Assert.True(result.Success);
        Assert.Equal(TransactionStatus.Unconfirmed, result.Data!.Status);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_Failed_HasFailureInfo()
    {
        var txId = "failed_tx";

        _provider.GetTransactionByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 100, 1700000000000, "FAILED", 0, 0, 0));

        _provider.GetTransactionInfoByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 100, 1700000000000, "FAILED", 500, 0, 0));

        var result = await _client.GetTransactionDetailAsync(txId);

        Assert.True(result.Success);
        Assert.Equal(TransactionStatus.Failed, result.Data!.Status);
        Assert.NotNull(result.Data.Failure);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_ProviderThrows_ReturnsFailResult()
    {
        _provider.GetTransactionByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("timeout"));

        var result = await _client.GetTransactionDetailAsync("some_tx");

        Assert.False(result.Success);
        Assert.Contains("timeout", result.Error!.Message);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_NativeTransfer_ParsesFromToAmount()
    {
        var txId = "native_tx";
        // owner_address and to_address are hex (41-prefix, 21 bytes = 42 hex chars)
        var ownerHex = "41a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
        var toHex = "41b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3";

        _provider.GetTransactionByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 100, 1700000000000, "SUCCESS", 0, 0, 0,
                ContractType: "TransferContract",
                OwnerAddress: ownerHex,
                ToAddress: toHex,
                AmountSun: 5_000_000)); // 5 TRX

        _provider.GetTransactionInfoByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 100, 1700000000000, "SUCCESS", 1000, 0, 300));

        var result = await _client.GetTransactionDetailAsync(txId);

        Assert.True(result.Success);
        var detail = result.Data!;
        Assert.Equal(TransactionType.NativeTransfer, detail.Type);
        Assert.Equal(5m, detail.Amount);
        // Addresses should be converted to base58
        Assert.StartsWith("T", detail.FromAddress);
        Assert.StartsWith("T", detail.ToAddress);
        Assert.NotEmpty(detail.FromAddress);
        Assert.NotEmpty(detail.ToAddress);
        Assert.Null(detail.TokenTransfer);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_Trc20Transfer_ParsesTokenInfo()
    {
        var txId = "trc20_tx";
        var ownerHex = "41a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
        var contractHex = "41b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3";
        // TRC20 transfer ABI data: a9059cbb + padded address (32 bytes) + padded amount (32 bytes)
        // Recipient: 41c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4 (strip 41 prefix for ABI encoding)
        // Amount: 1000000 (0xF4240)
        var data = "a9059cbb" +
                   "000000000000000000000000c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4" +
                   "00000000000000000000000000000000000000000000000000000000000f4240";

        _provider.GetTransactionByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 200, 1700000000000, "SUCCESS", 0, 0, 0,
                ContractType: "TriggerSmartContract",
                OwnerAddress: ownerHex,
                ToAddress: "",
                AmountSun: 0,
                ContractAddress: contractHex,
                ContractData: data));

        _provider.GetTransactionInfoByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 200, 1700000000000, "SUCCESS", 2000, 13000, 0));

        var result = await _client.GetTransactionDetailAsync(txId);

        Assert.True(result.Success);
        var detail = result.Data!;
        Assert.Equal(TransactionType.Trc20Transfer, detail.Type);
        Assert.StartsWith("T", detail.FromAddress);
        Assert.StartsWith("T", detail.ToAddress); // Recipient decoded from ABI data
        Assert.NotNull(detail.TokenTransfer);
        Assert.Equal(1000000m, detail.TokenTransfer!.Amount); // Raw amount (no decimals applied)
        Assert.StartsWith("T", detail.TokenTransfer.ContractAddress);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_ContractCall_NonTransfer()
    {
        var txId = "contract_call_tx";
        var ownerHex = "41a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
        var contractHex = "41b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3";
        // approve(address,uint256) selector = 095ea7b3
        var data = "095ea7b3" +
                   "000000000000000000000000c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4" +
                   "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";

        _provider.GetTransactionByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 300, 1700000000000, "SUCCESS", 0, 0, 0,
                ContractType: "TriggerSmartContract",
                OwnerAddress: ownerHex,
                ContractAddress: contractHex,
                ContractData: data));

        _provider.GetTransactionInfoByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 300, 1700000000000, "SUCCESS", 500, 5000, 0));

        var result = await _client.GetTransactionDetailAsync(txId);

        Assert.True(result.Success);
        Assert.Equal(TransactionType.ContractCall, result.Data!.Type);
        Assert.Null(result.Data.TokenTransfer);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_Stake_ParsesAmountAndType()
    {
        var txId = "stake_tx";
        var ownerHex = "41a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";

        _provider.GetTransactionByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 400, 1700000000000, "SUCCESS", 0, 0, 0,
                ContractType: "FreezeBalanceV2Contract",
                OwnerAddress: ownerHex,
                AmountSun: 100_000_000)); // 100 TRX

        _provider.GetTransactionInfoByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 400, 1700000000000, "SUCCESS", 0, 0, 300));

        var result = await _client.GetTransactionDetailAsync(txId);

        Assert.True(result.Success);
        Assert.Equal(TransactionType.Stake, result.Data!.Type);
        Assert.Equal(100m, result.Data.Amount);
        Assert.StartsWith("T", result.Data.FromAddress);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_ContractDeploy_ParsesType()
    {
        var txId = "deploy_tx";
        var ownerHex = "41a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";

        _provider.GetTransactionByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 500, 1700000000000, "SUCCESS", 0, 0, 0,
                ContractType: "CreateSmartContract",
                OwnerAddress: ownerHex));

        _provider.GetTransactionInfoByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 500, 1700000000000, "SUCCESS", 10000, 50000, 0));

        var result = await _client.GetTransactionDetailAsync(txId);

        Assert.True(result.Success);
        Assert.Equal(TransactionType.ContractDeploy, result.Data!.Type);
        Assert.StartsWith("T", result.Data.FromAddress);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_DelegateResource_ParsesType()
    {
        var txId = "delegate_tx";
        var ownerHex = "41a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
        var receiverHex = "41b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3";

        _provider.GetTransactionByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 600, 1700000000000, "SUCCESS", 0, 0, 0,
                ContractType: "DelegateResourceContract",
                OwnerAddress: ownerHex,
                ToAddress: receiverHex,
                AmountSun: 50_000_000)); // 50 TRX

        _provider.GetTransactionInfoByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 600, 1700000000000, "SUCCESS", 0, 0, 300));

        var result = await _client.GetTransactionDetailAsync(txId);

        Assert.True(result.Success);
        Assert.Equal(TransactionType.Delegate, result.Data!.Type);
        Assert.Equal(50m, result.Data.Amount);
        Assert.StartsWith("T", result.Data.FromAddress);
        Assert.StartsWith("T", result.Data.ToAddress);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_UnknownType_ReturnsOther()
    {
        var txId = "unknown_tx";

        _provider.GetTransactionByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 700, 1700000000000, "SUCCESS", 0, 0, 0,
                ContractType: "ProposalCreateContract",
                OwnerAddress: "41a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2"));

        _provider.GetTransactionInfoByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 700, 1700000000000, "SUCCESS", 0, 0, 0));

        var result = await _client.GetTransactionDetailAsync(txId);

        Assert.True(result.Success);
        Assert.Equal(TransactionType.Other, result.Data!.Type);
    }

    // === StakeTrxAsync ===

    [Fact]
    public async Task StakeTrxAsync_Success_ReturnsStakeResult()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(2000, "0000000000000200abcdef1234567890abcdef1234567890abcdef1234567890",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, new byte[34]));

        _provider.BroadcastTransactionAsync(
                Arg.Any<ChainKit.Tron.Protocol.Protobuf.Transaction>(),
                Arg.Any<CancellationToken>())
            .Returns(new BroadcastResult(true, "stake_tx_123", null));

        var result = await _client.StakeTrxAsync(account, 100m, ResourceType.Energy);

        Assert.True(result.Success);
        Assert.Equal("stake_tx_123", result.Data!.TxId);
        Assert.Equal(100m, result.Data.Amount);
        Assert.Equal(ResourceType.Energy, result.Data.Resource);
    }

    [Fact]
    public async Task StakeTrxAsync_BroadcastFails_ReturnsError()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(2000, "0000000000000200abcdef1234567890abcdef1234567890abcdef1234567890",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, new byte[34]));

        _provider.BroadcastTransactionAsync(
                Arg.Any<ChainKit.Tron.Protocol.Protobuf.Transaction>(),
                Arg.Any<CancellationToken>())
            .Returns(new BroadcastResult(false, null, "BALANCE_NOT_SUFFICIENT"));

        var result = await _client.StakeTrxAsync(account, 100m, ResourceType.Energy);

        Assert.False(result.Success);
    }

    // === UnstakeTrxAsync ===

    [Fact]
    public async Task UnstakeTrxAsync_Success_ReturnsUnstakeResult()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(3000, "0000000000000300abcdef1234567890abcdef1234567890abcdef1234567890",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, new byte[34]));

        _provider.BroadcastTransactionAsync(
                Arg.Any<ChainKit.Tron.Protocol.Protobuf.Transaction>(),
                Arg.Any<CancellationToken>())
            .Returns(new BroadcastResult(true, "unstake_tx_456", null));

        var result = await _client.UnstakeTrxAsync(account, 50m, ResourceType.Bandwidth);

        Assert.True(result.Success);
        Assert.Equal("unstake_tx_456", result.Data!.TxId);
        Assert.Equal(50m, result.Data.Amount);
        Assert.Equal(ResourceType.Bandwidth, result.Data.Resource);
    }

    // === DelegateResourceAsync ===

    [Fact]
    public async Task DelegateResourceAsync_Success_ReturnsDelegateResult()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(4000, "0000000000001000abcdef1234567890abcdef1234567890abcdef1234567890",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, new byte[34]));

        _provider.BroadcastTransactionAsync(
                Arg.Any<ChainKit.Tron.Protocol.Protobuf.Transaction>(),
                Arg.Any<CancellationToken>())
            .Returns(new BroadcastResult(true, "delegate_tx_789", null));

        var result = await _client.DelegateResourceAsync(
            account, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",
            20m, ResourceType.Energy, lockPeriod: true);

        Assert.True(result.Success);
        Assert.Equal("delegate_tx_789", result.Data!.TxId);
        Assert.Equal("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", result.Data.ReceiverAddress);
        Assert.Equal(20m, result.Data.Amount);
        Assert.Equal(ResourceType.Energy, result.Data.Resource);
    }

    // === UndelegateResourceAsync ===

    [Fact]
    public async Task UndelegateResourceAsync_Success_ReturnsUndelegateResult()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(5000, "0000000000001388abcdef1234567890abcdef1234567890abcdef1234567890",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, new byte[34]));

        _provider.BroadcastTransactionAsync(
                Arg.Any<ChainKit.Tron.Protocol.Protobuf.Transaction>(),
                Arg.Any<CancellationToken>())
            .Returns(new BroadcastResult(true, "undelegate_tx_abc", null));

        var result = await _client.UndelegateResourceAsync(
            account, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",
            10m, ResourceType.Bandwidth);

        Assert.True(result.Success);
        Assert.Equal("undelegate_tx_abc", result.Data!.TxId);
        Assert.Equal("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", result.Data.ReceiverAddress);
    }

    // === GetResourceInfoAsync ===

    [Fact]
    public async Task GetResourceInfoAsync_Success_ReturnsResourceInfo()
    {
        _provider.GetAccountResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountResourceInfo(
                FreeBandwidthLimit: 5000,
                FreeBandwidthUsed: 1000,
                EnergyLimit: 100000,
                EnergyUsed: 50000,
                TotalBandwidthLimit: 10000,
                TotalBandwidthUsed: 2000));

        var result = await _client.GetResourceInfoAsync("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t");

        Assert.True(result.Success);
        Assert.Equal(15000, result.Data!.BandwidthTotal); // 5000 + 10000
        Assert.Equal(3000, result.Data.BandwidthUsed);    // 1000 + 2000
        Assert.Equal(100000, result.Data.EnergyTotal);
        Assert.Equal(50000, result.Data.EnergyUsed);
    }

    [Fact]
    public async Task GetResourceInfoAsync_ProviderThrows_ReturnsFailResult()
    {
        _provider.GetAccountResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        var result = await _client.GetResourceInfoAsync("someaddr");

        Assert.False(result.Success);
        Assert.Contains("Network error", result.Error!.Message);
    }

    // === Error mapping ===

    [Fact]
    public async Task TransferTrxAsync_EnergyError_MapsToCorrectErrorCode()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(1000, "0000000000000100abcdef1234567890abcdef1234567890abcdef1234567890",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, new byte[34]));

        _provider.BroadcastTransactionAsync(
                Arg.Any<ChainKit.Tron.Protocol.Protobuf.Transaction>(),
                Arg.Any<CancellationToken>())
            .Returns(new BroadcastResult(false, null, "NOT_ENOUGH_ENERGY"));

        var result = await _client.TransferTrxAsync(account, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", 1m);

        Assert.False(result.Success);
        Assert.Equal("InsufficientEnergy", result.Error!.Code);
    }

    [Fact]
    public async Task TransferTrxAsync_DuplicateError_MapsToCorrectErrorCode()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(1000, "0000000000000100abcdef1234567890abcdef1234567890abcdef1234567890",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, new byte[34]));

        _provider.BroadcastTransactionAsync(
                Arg.Any<ChainKit.Tron.Protocol.Protobuf.Transaction>(),
                Arg.Any<CancellationToken>())
            .Returns(new BroadcastResult(false, null, "DUP_TRANSACTION_ERROR"));

        var result = await _client.TransferTrxAsync(account, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", 1m);

        Assert.False(result.Success);
        Assert.Equal("DuplicateTransaction", result.Error!.Code);
    }

    // === DeployContractAsync ===

    [Fact]
    public async Task DeployContractAsync_Success_ReturnsDeployResult()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);
        var bytecode = new byte[] { 0x60, 0x80, 0x60, 0x40 }; // dummy EVM bytecode
        var abi = "[{\"constant\":true,\"inputs\":[],\"name\":\"name\",\"outputs\":[{\"name\":\"\",\"type\":\"string\"}],\"type\":\"function\"}]";

        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(6000, "0000000000001770abcdef1234567890abcdef1234567890abcdef1234567890",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, new byte[34]));

        _provider.BroadcastTransactionAsync(
                Arg.Any<ChainKit.Tron.Protocol.Protobuf.Transaction>(),
                Arg.Any<CancellationToken>())
            .Returns(new BroadcastResult(true, "deploy_tx_123", null));

        var result = await _client.DeployContractAsync(account, bytecode, abi);

        Assert.True(result.Success);
        Assert.Equal("deploy_tx_123", result.Data!.TxId);
    }

    [Fact]
    public async Task DeployContractAsync_BroadcastFails_ReturnsError()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(6000, "0000000000001770abcdef1234567890abcdef1234567890abcdef1234567890",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, new byte[34]));

        _provider.BroadcastTransactionAsync(
                Arg.Any<ChainKit.Tron.Protocol.Protobuf.Transaction>(),
                Arg.Any<CancellationToken>())
            .Returns(new BroadcastResult(false, null, "NOT_ENOUGH_ENERGY"));

        var result = await _client.DeployContractAsync(account, new byte[] { 0x60 }, "[]");

        Assert.False(result.Success);
        Assert.Contains("ENERGY", result.Error!.Message);
    }

    [Fact]
    public async Task DeployContractAsync_ProviderThrows_ReturnsFailResult()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _client.DeployContractAsync(account, new byte[] { 0x60 }, "[]");

        Assert.False(result.Success);
        Assert.Contains("Connection refused", result.Error!.Message);
    }

    // === DeployTrc20TokenAsync ===

    [Fact]
    public async Task DeployTrc20TokenAsync_ProducesValidBytecodeAndAttemptsDeployment()
    {
        // With real template bytecode, DeployTrc20TokenAsync now proceeds to
        // DeployContractAsync, which calls Provider.GetNowBlockAsync.
        // Without mock setup for that call, it will fail at the provider level,
        // NOT with "not yet compiled".
        var account = TronAccount.FromPrivateKey(TestPrivateKey);
        var options = new Trc20TokenOptions("Test", "TST", 18, BigInteger.Parse("1000000000000000000000000"));

        var result = await _client.DeployTrc20TokenAsync(account, options);

        // The call should fail (mocked provider has no block setup),
        // but the error must NOT be the old "not yet compiled" message.
        Assert.False(result.Success);
        Assert.DoesNotContain("not yet compiled", result.Error!.Message);
    }

    // === GetTrc20Contract ===

    [Fact]
    public void GetTrc20Contract_ReturnsContractInstance()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);
        var contract = _client.GetTrc20Contract("41a614f803b6fd780986a42c78ec9c7f77e6ded13c", account);

        Assert.NotNull(contract);
        Assert.IsType<Trc20Contract>(contract);
        Assert.Equal("41a614f803b6fd780986a42c78ec9c7f77e6ded13c", contract.ContractAddress);
    }
}
