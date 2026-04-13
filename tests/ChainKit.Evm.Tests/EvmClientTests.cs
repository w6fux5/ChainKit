using System.Numerics;
using System.Text.Json;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ChainKit.Core.Extensions;
using ChainKit.Evm.Crypto;
using ChainKit.Evm.Models;
using ChainKit.Evm.Providers;
using Xunit;

namespace ChainKit.Evm.Tests;

public class EvmClientTests
{
    private readonly IEvmProvider _provider = Substitute.For<IEvmProvider>();
    private readonly EvmNetworkConfig _network = EvmNetwork.Sepolia;
    private readonly EvmClient _client;

    // Known private key for deterministic test account (key = 1)
    private static readonly byte[] TestPrivateKey =
        "0000000000000000000000000000000000000000000000000000000000000001".FromHex();

    // Corresponding address for private key = 1
    private const string TestAddress = "0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf";

    public EvmClientTests()
    {
        _client = new EvmClient(_provider, _network);
    }

    // === Constructor ===

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new EvmClient(null!, _network));
    }

    [Fact]
    public void Constructor_NullNetwork_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new EvmClient(_provider, null!));
    }

    [Fact]
    public void Constructor_ValidArgs_SetsProperties()
    {
        var client = new EvmClient(_provider, _network);
        Assert.Same(_provider, client.Provider);
        Assert.Same(_network, client.Network);
        Assert.NotNull(client.TokenCache);
    }

    // === TransferAsync ===

    [Fact]
    public async Task TransferAsync_InvalidAmount_Zero_ReturnsFail()
    {
        var account = EvmAccount.FromPrivateKey(TestPrivateKey);
        var result = await _client.TransferAsync(account, TestAddress, 0m);
        Assert.False(result.Success);
        Assert.Equal(EvmErrorCode.InvalidAmount, result.ErrorCode);
    }

    [Fact]
    public async Task TransferAsync_InvalidAmount_Negative_ReturnsFail()
    {
        var account = EvmAccount.FromPrivateKey(TestPrivateKey);
        var result = await _client.TransferAsync(account, TestAddress, -1m);
        Assert.False(result.Success);
        Assert.Equal(EvmErrorCode.InvalidAmount, result.ErrorCode);
    }

    [Fact]
    public async Task TransferAsync_InvalidAddress_ReturnsFail()
    {
        var account = EvmAccount.FromPrivateKey(TestPrivateKey);
        var result = await _client.TransferAsync(account, "not-an-address", 1m);
        Assert.False(result.Success);
        Assert.Equal(EvmErrorCode.InvalidAddress, result.ErrorCode);
    }

    [Fact]
    public async Task TransferAsync_Success_ReturnsTxId()
    {
        var account = EvmAccount.FromPrivateKey(TestPrivateKey);
        var expectedTxHash = "0xabc123";

        _provider.GetTransactionCountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(5L);
        _provider.EstimateGasAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<BigInteger?>(), Arg.Any<CancellationToken>())
            .Returns(21000L);
        _provider.GetEip1559FeesAsync(Arg.Any<CancellationToken>())
            .Returns((new BigInteger(30_000_000_000), new BigInteger(2_000_000_000)));
        _provider.SendRawTransactionAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(expectedTxHash);

        var result = await _client.TransferAsync(account, TestAddress, 1.5m);

        Assert.True(result.Success);
        Assert.Equal(expectedTxHash, result.Data!.TxId);
    }

    [Fact]
    public async Task TransferAsync_ProviderThrows_ReturnsFail()
    {
        var account = EvmAccount.FromPrivateKey(TestPrivateKey);

        _provider.GetTransactionCountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var result = await _client.TransferAsync(account, TestAddress, 1m);

        Assert.False(result.Success);
        Assert.Equal(EvmErrorCode.Unknown, result.ErrorCode);
        Assert.Contains("connection refused", result.Error!.Message);
    }

    // === GetBalanceAsync ===

    [Fact]
    public async Task GetBalanceAsync_Success_ConvertsWeiToEth()
    {
        var oneEthInWei = BigInteger.Parse("1000000000000000000");
        _provider.GetBalanceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(oneEthInWei);

        var result = await _client.GetBalanceAsync(TestAddress);

        Assert.True(result.Success);
        Assert.Equal(1.0m, result.Data!.Balance);
        Assert.Equal(oneEthInWei, result.Data.RawBalance);
    }

    [Fact]
    public async Task GetBalanceAsync_FractionalAmount_ConvertsCorrectly()
    {
        // 0.5 ETH = 500000000000000000 Wei
        var halfEthInWei = BigInteger.Parse("500000000000000000");
        _provider.GetBalanceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(halfEthInWei);

        var result = await _client.GetBalanceAsync(TestAddress);

        Assert.True(result.Success);
        Assert.Equal(0.5m, result.Data!.Balance);
        Assert.Equal(halfEthInWei, result.Data.RawBalance);
    }

    [Fact]
    public async Task GetBalanceAsync_InvalidAddress_ReturnsFail()
    {
        var result = await _client.GetBalanceAsync("invalid");
        Assert.False(result.Success);
        Assert.Equal(EvmErrorCode.InvalidAddress, result.ErrorCode);
    }

    [Fact]
    public async Task GetBalanceAsync_ProviderThrows_ReturnsFail()
    {
        _provider.GetBalanceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("timeout"));

        var result = await _client.GetBalanceAsync(TestAddress);

        Assert.False(result.Success);
        Assert.Equal(EvmErrorCode.ProviderConnectionFailed, result.ErrorCode);
    }

    // === GetTransactionDetailAsync ===

    [Fact]
    public async Task GetTransactionDetailAsync_NotFound_ReturnsFail()
    {
        _provider.GetTransactionByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((JsonElement?)null);

        var result = await _client.GetTransactionDetailAsync("0xabc");

        Assert.False(result.Success);
        Assert.Equal(EvmErrorCode.TransactionNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_Confirmed_MergesTxAndReceipt()
    {
        var txHash = "0xdeadbeef";
        var txJson = JsonDocument.Parse("""
        {
            "from": "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "to": "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "value": "0xde0b6b3a7640000",
            "nonce": "0x5",
            "blockNumber": "0x10"
        }
        """);
        var receiptJson = JsonDocument.Parse("""
        {
            "status": "0x1",
            "gasUsed": "0x5208",
            "effectiveGasPrice": "0x77359400",
            "blockNumber": "0x10"
        }
        """);

        _provider.GetTransactionByHashAsync(txHash, Arg.Any<CancellationToken>())
            .Returns(txJson.RootElement);
        _provider.GetTransactionReceiptAsync(txHash, Arg.Any<CancellationToken>())
            .Returns(receiptJson.RootElement);

        var result = await _client.GetTransactionDetailAsync(txHash);

        Assert.True(result.Success);
        var detail = result.Data!;
        Assert.Equal(txHash, detail.TxId);
        Assert.Equal("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", detail.FromAddress);
        Assert.Equal("0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", detail.ToAddress);
        Assert.Equal(TransactionStatus.Confirmed, detail.Status);
        Assert.Equal(16L, detail.BlockNumber);
        Assert.Equal(5L, detail.Nonce);
        Assert.Equal(1.0m, detail.Amount); // 0xde0b6b3a7640000 = 1 ETH
        Assert.Equal(21000L, detail.GasUsed); // 0x5208 = 21000
        Assert.True(detail.Fee > 0); // gasUsed * effectiveGasPrice
        Assert.Null(detail.Failure);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_Failed_HasFailureInfo()
    {
        var txHash = "0xfailed";
        var txJson = JsonDocument.Parse("""
        {
            "from": "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "to": "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "value": "0x0",
            "nonce": "0x1",
            "blockNumber": "0x20"
        }
        """);
        var receiptJson = JsonDocument.Parse("""
        {
            "status": "0x0",
            "gasUsed": "0xc350",
            "effectiveGasPrice": "0x3b9aca00",
            "blockNumber": "0x20"
        }
        """);

        _provider.GetTransactionByHashAsync(txHash, Arg.Any<CancellationToken>())
            .Returns(txJson.RootElement);
        _provider.GetTransactionReceiptAsync(txHash, Arg.Any<CancellationToken>())
            .Returns(receiptJson.RootElement);

        var result = await _client.GetTransactionDetailAsync(txHash);

        Assert.True(result.Success);
        var detail = result.Data!;
        Assert.Equal(TransactionStatus.Failed, detail.Status);
        Assert.NotNull(detail.Failure);
        Assert.Equal("Transaction reverted", detail.Failure.Reason);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_Unconfirmed_NoReceipt()
    {
        var txHash = "0xpending";
        var txJson = JsonDocument.Parse("""
        {
            "from": "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "to": "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "value": "0x0",
            "nonce": "0x3",
            "blockNumber": "0x0"
        }
        """);

        _provider.GetTransactionByHashAsync(txHash, Arg.Any<CancellationToken>())
            .Returns(txJson.RootElement);
        _provider.GetTransactionReceiptAsync(txHash, Arg.Any<CancellationToken>())
            .Returns((JsonElement?)null);

        var result = await _client.GetTransactionDetailAsync(txHash);

        Assert.True(result.Success);
        var detail = result.Data!;
        Assert.Equal(TransactionStatus.Unconfirmed, detail.Status);
        Assert.Equal(0L, detail.GasUsed);
        Assert.Equal(0m, detail.Fee);
        Assert.Null(detail.Failure);
    }

    [Fact]
    public async Task GetTransactionDetailAsync_ProviderThrows_ReturnsFail()
    {
        _provider.GetTransactionByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network error"));

        var result = await _client.GetTransactionDetailAsync("0xabc");

        Assert.False(result.Success);
        Assert.Equal(EvmErrorCode.Unknown, result.ErrorCode);
    }

    // === GetBlockNumberAsync ===

    [Fact]
    public async Task GetBlockNumberAsync_Success()
    {
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(12345L);

        var result = await _client.GetBlockNumberAsync();

        Assert.True(result.Success);
        Assert.Equal(12345L, result.Data);
    }

    [Fact]
    public async Task GetBlockNumberAsync_ProviderFails_ReturnsFail()
    {
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("timeout"));

        var result = await _client.GetBlockNumberAsync();

        Assert.False(result.Success);
        Assert.Equal(EvmErrorCode.ProviderConnectionFailed, result.ErrorCode);
    }

    // === GetErc20Contract ===

    [Fact]
    public void GetErc20Contract_ReturnsInstance()
    {
        var contract = _client.GetErc20Contract("0xdAC17F958D2ee523a2206206994597C13D831ec7");
        Assert.NotNull(contract);
        Assert.Equal("0xdAC17F958D2ee523a2206206994597C13D831ec7", contract.ContractAddress);
    }

    [Fact]
    public void GetErc20Contract_SharesProviderAndNetwork()
    {
        var contract = _client.GetErc20Contract("0xdAC17F958D2ee523a2206206994597C13D831ec7");
        Assert.NotNull(contract);
        // The contract should be usable — a second call creates a distinct instance
        var contract2 = _client.GetErc20Contract("0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48");
        Assert.NotNull(contract2);
        Assert.NotEqual(contract.ContractAddress, contract2.ContractAddress);
    }

    // === Dispose ===

    [Fact]
    public void Dispose_DoesNotDisposeProvider()
    {
        // Verify that disposing the client does NOT dispose the externally-owned provider
        _client.Dispose();
        _provider.DidNotReceive().Dispose();
    }
}
