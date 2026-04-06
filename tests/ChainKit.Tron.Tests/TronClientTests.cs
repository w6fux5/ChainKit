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
    public async Task TransferTrxAsync_NegativeAmount_ReturnsFail()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        var result = await _client.TransferTrxAsync(account, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", -5m);

        Assert.False(result.Success);
        Assert.Contains("Amount must be positive", result.Error!.Message);
    }

    [Fact]
    public async Task TransferTrxAsync_ZeroAmount_ReturnsFail()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        var result = await _client.TransferTrxAsync(account, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", 0m);

        Assert.False(result.Success);
        Assert.Contains("Amount must be positive", result.Error!.Message);
    }

    [Fact]
    public async Task TransferTrxAsync_OverflowAmount_ReturnsFail()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        // decimal.MaxValue will overflow when multiplied by 1_000_000 and cast to long
        var result = await _client.TransferTrxAsync(account, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", decimal.MaxValue);

        Assert.False(result.Success);
        Assert.Contains("Amount too large", result.Error!.Message);
    }

    [Fact]
    public async Task StakeTrxAsync_NegativeAmount_ReturnsFail()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        var result = await _client.StakeTrxAsync(account, -1m, ResourceType.Bandwidth);

        Assert.False(result.Success);
        Assert.Contains("Amount must be positive", result.Error!.Message);
    }

    [Fact]
    public async Task UnstakeTrxAsync_NegativeAmount_ReturnsFail()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        var result = await _client.UnstakeTrxAsync(account, -1m, ResourceType.Energy);

        Assert.False(result.Success);
        Assert.Contains("Amount must be positive", result.Error!.Message);
    }

    [Fact]
    public async Task DelegateResourceAsync_NegativeAmount_ReturnsFail()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        var result = await _client.DelegateResourceAsync(account, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", -1m, ResourceType.Bandwidth);

        Assert.False(result.Success);
        Assert.Contains("Amount must be positive", result.Error!.Message);
    }

    [Fact]
    public async Task UndelegateResourceAsync_NegativeAmount_ReturnsFail()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        var result = await _client.UndelegateResourceAsync(account, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", -1m, ResourceType.Energy);

        Assert.False(result.Success);
        Assert.Contains("Amount must be positive", result.Error!.Message);
    }

    [Fact]
    public async Task TransferTrc20Async_NegativeAmount_ReturnsFail()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        var result = await _client.TransferTrc20Async(account, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",
            "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", -10m, 6);

        Assert.False(result.Success);
        Assert.Contains("Amount must be positive", result.Error!.Message);
    }

    [Fact]
    public async Task TransferTrc20Async_ZeroAmount_ReturnsFail()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        var result = await _client.TransferTrc20Async(account, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",
            "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", 0m, 6);

        Assert.False(result.Success);
        Assert.Contains("Amount must be positive", result.Error!.Message);
    }

    [Fact]
    public async Task TransferTrc20Async_InvalidDecimals_ReturnsFail()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        var result = await _client.TransferTrc20Async(account, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",
            "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", 100m, -1);

        Assert.False(result.Success);
        Assert.Contains("Invalid decimals", result.Error!.Message);
    }

    [Fact]
    public async Task StakeTrxAsync_OverflowAmount_ReturnsFail()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        var result = await _client.StakeTrxAsync(account, decimal.MaxValue, ResourceType.Energy);

        Assert.False(result.Success);
        Assert.Contains("Amount too large", result.Error!.Message);
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
        Assert.Equal(0m, result.Data.Trc20Balances["TRC20ContractAddr"].RawBalance);
    }

    [Fact]
    public async Task GetBalanceAsync_Trc20KnownToken_ConvertsByDecimals()
    {
        _provider.GetAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountInfo("41abc", 10_000_000, 0, 0, 0)); // 10 TRX

        // Return 20_200_000 raw USDT balance (6 decimals -> 20.2 USDT)
        var rawBalance = AbiEncoder.EncodeUint256(new BigInteger(20_200_000));
        _provider.TriggerConstantContractAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(rawBalance);

        // USDT mainnet contract address (known token, 6 decimals)
        var usdtAddr = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";
        var result = await _client.GetBalanceAsync("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", usdtAddr);

        Assert.True(result.Success);
        Assert.Equal(10m, result.Data!.TrxBalance);
        // Raw balance is always present
        Assert.Equal(20_200_000m, result.Data.Trc20Balances[usdtAddr].RawBalance);
        // 20_200_000 / 10^6 = 20.2
        Assert.Equal(20.2m, result.Data.Trc20Balances[usdtAddr].Balance);
        Assert.Equal("USDT", result.Data.Trc20Balances[usdtAddr].Symbol);
        Assert.Equal(6, result.Data.Trc20Balances[usdtAddr].Decimals);

        // Provider should NOT have been called for symbol/decimals (known token)
        await _provider.DidNotReceive().TriggerConstantContractAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<string>(s => s == "symbol()" || s == "decimals()"),
            Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
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
        Assert.Equal(0m, result.Data.Trc20Balances["SomeTRC20"].RawBalance);
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
    public async Task GetTransactionDetailAsync_ResourceCost_ParsesEnergyAndBandwidthTrxCosts()
    {
        var txId = "resource_cost_tx";

        _provider.GetTransactionByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 100, 1700000000000, "SUCCESS", 0, 0, 0,
                ContractType: "TriggerSmartContract",
                OwnerAddress: "41a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2"));

        // energy_fee = 27_255_900 sun, net_fee = 348_000 sun
        _provider.GetTransactionInfoByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 100, 1700000000000, "SUCCESS",
                Fee: 27_603_900, EnergyUsage: 64_895, NetUsage: 345,
                EnergyFee: 27_255_900, NetFee: 348_000));

        var result = await _client.GetTransactionDetailAsync(txId);

        Assert.True(result.Success);
        var cost = result.Data!.Cost!;
        Assert.Equal(27_603_900m / 1_000_000m, cost.TrxBurned);
        Assert.Equal(64_895, cost.EnergyUsed);
        Assert.Equal(345, cost.BandwidthUsed);
        Assert.Equal(27_255_900m / 1_000_000m, cost.EnergyTrxCost);
        Assert.Equal(348_000m / 1_000_000m, cost.BandwidthTrxCost);
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
    public async Task GetTransactionDetailAsync_Unconfirmed_WhenSolidityReturnsEmptyTxId()
    {
        // 真實場景：Solidity Node 回傳空物件 {}，ParseTransactionInfo 解析後 TxId 為空
        var txId = "pending_tx";

        _provider.GetTransactionByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 100, 1700000000000, "", 0, 0, 0));

        // 模擬 Solidity Node 回傳空物件：TxId 為空，代表交易尚未 solidified
        _provider.GetTransactionInfoByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto("", 0, 0, "", 0, 0, 0));

        var result = await _client.GetTransactionDetailAsync(txId);

        Assert.True(result.Success);
        Assert.Equal(TransactionStatus.Unconfirmed, result.Data!.Status);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_SmartContract_Revert_ReturnsFailed()
    {
        var txId = "revert_tx";

        _provider.GetTransactionByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 100, 1700000000000, "", 0, 0, 0,
                ContractType: "TriggerSmartContract"));

        _provider.GetTransactionInfoByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 100, 1700000000000, "", 5000000, 100000, 0,
                ReceiptResult: "REVERT"));

        var result = await _client.GetTransactionDetailAsync(txId);

        Assert.True(result.Success);
        Assert.Equal(TransactionStatus.Failed, result.Data!.Status);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_SmartContract_OutOfEnergy_ReturnsFailed()
    {
        var txId = "oom_tx";

        _provider.GetTransactionByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 100, 1700000000000, "", 0, 0, 0,
                ContractType: "TriggerSmartContract"));

        _provider.GetTransactionInfoByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 100, 1700000000000, "", 5000000, 100000, 0,
                ReceiptResult: "OUT_OF_ENERGY"));

        var result = await _client.GetTransactionDetailAsync(txId);

        Assert.True(result.Success);
        Assert.Equal(TransactionStatus.Failed, result.Data!.Status);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_SmartContract_Success_ReturnsConfirmed()
    {
        var txId = "success_tx";

        _provider.GetTransactionByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 100, 1700000000000, "", 0, 0, 0,
                ContractType: "TriggerSmartContract"));

        _provider.GetTransactionInfoByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 100, 1700000000000, "", 5000000, 50000, 0,
                ReceiptResult: "SUCCESS"));

        var result = await _client.GetTransactionDetailAsync(txId);

        Assert.True(result.Success);
        Assert.Equal(TransactionStatus.Confirmed, result.Data!.Status);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_SystemContract_NoReceipt_ReturnsConfirmed()
    {
        // System Contract（TRX 轉帳）沒有 receipt.result，查到就是 Confirmed
        var txId = "trx_transfer_tx";

        _provider.GetTransactionByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 100, 1700000000000, "", 0, 0, 0,
                ContractType: "TransferContract"));

        _provider.GetTransactionInfoByIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto(txId, 100, 1700000000000, "", 0, 0, 267));

        var result = await _client.GetTransactionDetailAsync(txId);

        Assert.True(result.Success);
        Assert.Equal(TransactionStatus.Confirmed, result.Data!.Status);
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

        // Mock symbol() and decimals() contract calls for the unknown contract
        var symbolBytes = BuildAbiString("TTKN");
        _provider.TriggerConstantContractAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is<string>(s => s == "symbol()"),
                Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(symbolBytes);

        var decimalsBytes = AbiEncoder.EncodeUint256(new BigInteger(6));
        _provider.TriggerConstantContractAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is<string>(s => s == "decimals()"),
                Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(decimalsBytes);

        var result = await _client.GetTransactionDetailAsync(txId);

        Assert.True(result.Success);
        var detail = result.Data!;
        Assert.Equal(TransactionType.Trc20Transfer, detail.Type);
        Assert.StartsWith("T", detail.FromAddress);
        Assert.StartsWith("T", detail.ToAddress); // Recipient decoded from ABI data
        Assert.NotNull(detail.TokenTransfer);
        Assert.Equal("TTKN", detail.TokenTransfer!.Symbol);
        Assert.Equal(6, detail.TokenTransfer.Decimals);
        Assert.Equal(1_000_000m, detail.TokenTransfer.RawAmount); // raw on-chain value
        Assert.Equal(1m, detail.TokenTransfer.Amount); // 1000000 / 10^6 = 1.0
        Assert.StartsWith("T", detail.TokenTransfer.ContractAddress);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_Trc20Transfer_KnownUsdtToken_ResolvesWithoutProviderCall()
    {
        var txId = "usdt_tx";
        var ownerHex = "41a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
        // USDT mainnet contract address
        var contractHex = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
        // Amount: 20200000 raw = 20.2 USDT (6 decimals). 20200000 = 0x1343A40
        var data = "a9059cbb" +
                   "000000000000000000000000c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4" +
                   "0000000000000000000000000000000000000000000000000000000001343a40";

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
        Assert.Equal("USDT", detail.TokenTransfer!.Symbol);
        Assert.Equal(6, detail.TokenTransfer.Decimals);
        Assert.Equal(20_200_000m, detail.TokenTransfer.RawAmount); // raw on-chain value
        Assert.Equal(20.2m, detail.TokenTransfer.Amount);

        // Provider should NOT have been called for symbol/decimals (known token)
        await _provider.DidNotReceive().TriggerConstantContractAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<string>(s => s == "symbol()" || s == "decimals()"),
            Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
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
        _provider.GetAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountInfo(
                Address: "41a1b2c3d4e5f60000000000000000000000000001",
                Balance: 50_000_000,
                NetUsage: 0,
                EnergyUsage: 0,
                CreateTime: 1609459200000,
                FrozenBalanceForBandwidth: 10_000_000, // 10 TRX staked for bandwidth
                FrozenBalanceForEnergy: 25_000_000));  // 25 TRX staked for energy

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
        Assert.Equal(10m, result.Data.StakedForBandwidth);  // 10_000_000 SUN = 10 TRX
        Assert.Equal(25m, result.Data.StakedForEnergy);     // 25_000_000 SUN = 25 TRX
    }

    [Fact]
    public async Task GetResourceInfoAsync_ZeroStaking_ReturnsZeroStakedAmounts()
    {
        _provider.GetAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountInfo(
                Address: "41a1b2c3d4e5f60000000000000000000000000001",
                Balance: 1_000_000, NetUsage: 0, EnergyUsage: 0, CreateTime: 0));

        _provider.GetAccountResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountResourceInfo(600, 0, 0, 0, 0, 0));

        var result = await _client.GetResourceInfoAsync("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t");

        Assert.True(result.Success);
        Assert.Equal(0m, result.Data!.StakedForBandwidth);
        Assert.Equal(0m, result.Data.StakedForEnergy);
    }

    [Fact]
    public async Task GetResourceInfoAsync_ProviderThrows_ReturnsFailResult()
    {
        _provider.GetAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));
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

    // === GetResourceInfoAsync with Delegations ===

    [Fact]
    public async Task GetResourceInfoAsync_WithDelegations_ReturnsDelegationInfo()
    {
        var ownerHex = "41a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
        var delegateeHex = "41b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3";
        var delegatorHex = "41c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";

        _provider.GetAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountInfo(ownerHex, 50_000_000, 0, 0, 0,
                FrozenBalanceForBandwidth: 10_000_000,
                FrozenBalanceForEnergy: 25_000_000));

        _provider.GetAccountResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountResourceInfo(5000, 1000, 100000, 50000, 10000, 2000));

        _provider.GetDelegatedResourceAccountIndexAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DelegatedResourceIndex(
                ToAddresses: new[] { delegateeHex },
                FromAddresses: new[] { delegatorHex }));

        // Delegation OUT: owner delegated 5 TRX bandwidth to delegatee
        _provider.GetDelegatedResourceAsync(
                Arg.Any<string>(), Arg.Is(delegateeHex), Arg.Any<CancellationToken>())
            .Returns(new List<DelegatedResourceInfo>
            {
                new DelegatedResourceInfo(ownerHex, delegateeHex, 5_000_000, 0)
            });

        // Delegation IN: delegator delegated 3 TRX energy to owner
        _provider.GetDelegatedResourceAsync(
                Arg.Is(delegatorHex), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<DelegatedResourceInfo>
            {
                new DelegatedResourceInfo(delegatorHex, ownerHex, 0, 3_000_000)
            });

        var result = await _client.GetResourceInfoAsync(ownerHex);

        Assert.True(result.Success);
        var info = result.Data!;

        // Basic resource info still works
        Assert.Equal(15000, info.BandwidthTotal);
        Assert.Equal(100000, info.EnergyTotal);
        Assert.Equal(10m, info.StakedForBandwidth);
        Assert.Equal(25m, info.StakedForEnergy);

        // Delegations OUT
        Assert.Single(info.DelegationsOut);
        Assert.Equal(5m, info.DelegationsOut[0].Amount);
        Assert.Equal(ResourceType.Bandwidth, info.DelegationsOut[0].Resource);
        Assert.StartsWith("T", info.DelegationsOut[0].Address);

        // Delegations IN
        Assert.Single(info.DelegationsIn);
        Assert.Equal(3m, info.DelegationsIn[0].Amount);
        Assert.Equal(ResourceType.Energy, info.DelegationsIn[0].Resource);
        Assert.StartsWith("T", info.DelegationsIn[0].Address);
    }

    [Fact]
    public async Task GetResourceInfoAsync_DelegationQueryFails_StillReturnsResourceInfo()
    {
        _provider.GetAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountInfo("41abc", 50_000_000, 0, 0, 0,
                FrozenBalanceForBandwidth: 10_000_000,
                FrozenBalanceForEnergy: 25_000_000));

        _provider.GetAccountResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountResourceInfo(5000, 1000, 100000, 50000, 10000, 2000));

        // Delegation index query fails
        _provider.GetDelegatedResourceAccountIndexAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("endpoint not available"));

        var result = await _client.GetResourceInfoAsync("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t");

        Assert.True(result.Success);
        var info = result.Data!;
        Assert.Equal(15000, info.BandwidthTotal);
        Assert.Equal(100000, info.EnergyTotal);
        Assert.Equal(10m, info.StakedForBandwidth);
        Assert.Equal(25m, info.StakedForEnergy);
        // Delegations should be empty but not cause failure
        Assert.Empty(info.DelegationsOut);
        Assert.Empty(info.DelegationsIn);
    }

    [Fact]
    public async Task GetResourceInfoAsync_MultipleDelegations_AggregatesCorrectly()
    {
        var ownerHex = "41a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
        var addr1 = "41b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3";
        var addr2 = "41c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";

        _provider.GetAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountInfo(ownerHex, 1_000_000, 0, 0, 0));

        _provider.GetAccountResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountResourceInfo(600, 0, 0, 0, 0, 0));

        _provider.GetDelegatedResourceAccountIndexAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DelegatedResourceIndex(
                ToAddresses: new[] { addr1, addr2 },
                FromAddresses: Array.Empty<string>()));

        // Delegations to addr1: both bandwidth and energy
        _provider.GetDelegatedResourceAsync(
                Arg.Any<string>(), Arg.Is(addr1), Arg.Any<CancellationToken>())
            .Returns(new List<DelegatedResourceInfo>
            {
                new DelegatedResourceInfo(ownerHex, addr1, 2_000_000, 3_000_000)
            });

        // Delegations to addr2: energy only
        _provider.GetDelegatedResourceAsync(
                Arg.Any<string>(), Arg.Is(addr2), Arg.Any<CancellationToken>())
            .Returns(new List<DelegatedResourceInfo>
            {
                new DelegatedResourceInfo(ownerHex, addr2, 0, 7_000_000)
            });

        var result = await _client.GetResourceInfoAsync(ownerHex);

        Assert.True(result.Success);
        // addr1 produces 2 entries (bandwidth + energy), addr2 produces 1 entry (energy)
        Assert.Equal(3, result.Data!.DelegationsOut.Count);
        Assert.Empty(result.Data.DelegationsIn);

        // Check specific entries
        var bwDelegation = result.Data.DelegationsOut.First(d => d.Resource == ResourceType.Bandwidth);
        Assert.Equal(2m, bwDelegation.Amount);

        var energyDelegations = result.Data.DelegationsOut.Where(d => d.Resource == ResourceType.Energy).ToList();
        Assert.Equal(2, energyDelegations.Count);
        Assert.Contains(energyDelegations, d => d.Amount == 3m);
        Assert.Contains(energyDelegations, d => d.Amount == 7m);
    }

    // === IDisposable ===

    [Fact]
    public void Dispose_DisposesDisposableProvider()
    {
        var disposableProvider = Substitute.For<ITronProvider, IDisposable>();
        var client = new TronClient(disposableProvider);

        client.Dispose();

        ((IDisposable)disposableProvider).Received(1).Dispose();
    }

    [Fact]
    public void Dispose_NonDisposableProvider_DoesNotThrow()
    {
        // ITronProvider alone is not IDisposable; Dispose should still succeed
        var provider = Substitute.For<ITronProvider>();
        var client = new TronClient(provider);

        var ex = Record.Exception(() => client.Dispose());

        Assert.Null(ex);
    }

    // === Overflow validation for remaining methods ===

    [Fact]
    public async Task UnstakeTrxAsync_OverflowAmount_ReturnsFail()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        var result = await _client.UnstakeTrxAsync(account, decimal.MaxValue, ResourceType.Bandwidth);

        Assert.False(result.Success);
        Assert.Contains("Amount too large", result.Error!.Message);
    }

    [Fact]
    public async Task DelegateResourceAsync_OverflowAmount_ReturnsFail()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        var result = await _client.DelegateResourceAsync(
            account, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", decimal.MaxValue, ResourceType.Energy);

        Assert.False(result.Success);
        Assert.Contains("Amount too large", result.Error!.Message);
    }

    [Fact]
    public async Task UndelegateResourceAsync_OverflowAmount_ReturnsFail()
    {
        var account = TronAccount.FromPrivateKey(TestPrivateKey);

        var result = await _client.UndelegateResourceAsync(
            account, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", decimal.MaxValue, ResourceType.Bandwidth);

        Assert.False(result.Success);
        Assert.Contains("Amount too large", result.Error!.Message);
    }

    // === Helpers ===

    /// <summary>
    /// Builds an ABI-encoded string return value (offset + length + data padded to 32 bytes).
    /// </summary>
    private static byte[] BuildAbiString(string value)
    {
        var strBytes = System.Text.Encoding.UTF8.GetBytes(value);
        var paddedLen = ((strBytes.Length + 31) / 32) * 32;
        var result = new byte[32 + 32 + paddedLen];
        result[31] = 0x20; // offset = 32
        var lenBytes = AbiEncoder.EncodeUint256(new BigInteger(strBytes.Length));
        Buffer.BlockCopy(lenBytes, 0, result, 32, 32);
        Buffer.BlockCopy(strBytes, 0, result, 64, strBytes.Length);
        return result;
    }
}
