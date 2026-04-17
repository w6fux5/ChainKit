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

    // === WaitForReceiptAsync ===

    private static JsonElement BuildReceipt(string status = "0x1", string blockNumber = "0x10")
    {
        var json = $"{{\"status\":\"{status}\",\"blockNumber\":\"{blockNumber}\",\"gasUsed\":\"0x5208\",\"effectiveGasPrice\":\"0x1\"}}";
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task WaitForReceiptAsync_ReceiptAppearsAfterTwoPolls_ReturnsOk()
    {
        var calls = 0;
        _provider.GetTransactionReceiptAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                return Task.FromResult<JsonElement?>(calls < 3 ? null : BuildReceipt());
            });

        var result = await _client.WaitForReceiptAsync("0xabc",
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(10));

        Assert.True(result.Success);
        Assert.Equal("0x1", result.Data.GetProperty("status").GetString());
        Assert.Equal(3, calls);
        await _provider.DidNotReceive().GetTransactionByHashAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WaitForReceiptAsync_NeverMined_TimesOut()
    {
        _provider.GetTransactionReceiptAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((JsonElement?)null);

        var result = await _client.WaitForReceiptAsync("0xT",
            timeout: TimeSpan.FromMilliseconds(50),
            pollInterval: TimeSpan.FromMilliseconds(10));

        Assert.False(result.Success);
        Assert.Equal(EvmErrorCode.ProviderTimeout, result.ErrorCode);
    }

    [Fact]
    public async Task WaitForReceiptAsync_ConsecutiveFailures_ReturnsProviderConnectionFailed()
    {
        _provider.GetTransactionReceiptAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network down"));

        var result = await _client.WaitForReceiptAsync("0xF",
            timeout: TimeSpan.FromSeconds(10),
            pollInterval: TimeSpan.FromMilliseconds(10),
            maxConsecutiveFailures: 3);

        Assert.False(result.Success);
        Assert.Equal(EvmErrorCode.ProviderConnectionFailed, result.ErrorCode);
    }

    [Fact]
    public async Task WaitForReceiptAsync_Cancelled_ThrowsOperationCanceled()
    {
        _provider.GetTransactionReceiptAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((JsonElement?)null);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _client.WaitForReceiptAsync("0xC",
                timeout: TimeSpan.FromSeconds(5),
                pollInterval: TimeSpan.FromMilliseconds(5),
                ct: cts.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WaitForReceiptAsync_BadTxHash_ReturnsInvalidArgument(string? txHash)
    {
        var result = await _client.WaitForReceiptAsync(txHash!);

        Assert.False(result.Success);
        Assert.Equal(EvmErrorCode.InvalidArgument, result.ErrorCode);
        await _provider.DidNotReceive().GetTransactionReceiptAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WaitForReceiptAsync_ZeroPollInterval_ReturnsInvalidArgument()
    {
        var result = await _client.WaitForReceiptAsync("0xZP", pollInterval: TimeSpan.Zero);
        Assert.False(result.Success);
        Assert.Equal(EvmErrorCode.InvalidArgument, result.ErrorCode);
    }

    [Fact]
    public async Task WaitForReceiptAsync_NegativeTimeout_ReturnsInvalidArgument()
    {
        var result = await _client.WaitForReceiptAsync("0xNT", timeout: TimeSpan.FromSeconds(-1));
        Assert.False(result.Success);
        Assert.Equal(EvmErrorCode.InvalidArgument, result.ErrorCode);
    }

    [Fact]
    public async Task WaitForReceiptAsync_NegativeMaxFailures_ReturnsInvalidArgument()
    {
        var result = await _client.WaitForReceiptAsync("0xNF", maxConsecutiveFailures: -1);
        Assert.False(result.Success);
        Assert.Equal(EvmErrorCode.InvalidArgument, result.ErrorCode);
    }

    [Fact]
    public async Task WaitForReceiptAsync_FailureThenSuccess_ResetsCounter()
    {
        var calls = 0;
        _provider.GetTransactionReceiptAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                if (calls <= 2) throw new HttpRequestException("flaky");
                if (calls == 3) return Task.FromResult<JsonElement?>(null);
                return Task.FromResult<JsonElement?>(BuildReceipt());
            });

        var result = await _client.WaitForReceiptAsync("0xR",
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(10),
            maxConsecutiveFailures: 3);

        // 2 throws then success — counter resets, never hits the threshold.
        Assert.True(result.Success);
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task WaitForReceiptAsync_MaxFailuresZero_RetriesUntilTimeout()
    {
        _provider.GetTransactionReceiptAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("always down"));

        var result = await _client.WaitForReceiptAsync("0xU",
            timeout: TimeSpan.FromMilliseconds(50),
            pollInterval: TimeSpan.FromMilliseconds(10),
            maxConsecutiveFailures: 0);

        Assert.False(result.Success);
        Assert.Equal(EvmErrorCode.ProviderTimeout, result.ErrorCode);
    }

    [Fact]
    public async Task WaitForReceiptAsync_TimeoutZero_PollsOnceThenTimesOut()
    {
        var calls = 0;
        _provider.GetTransactionReceiptAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { calls++; return Task.FromResult<JsonElement?>(null); });

        var result = await _client.WaitForReceiptAsync("0xZ0",
            timeout: TimeSpan.Zero,
            pollInterval: TimeSpan.FromMilliseconds(10));

        Assert.False(result.Success);
        Assert.Equal(EvmErrorCode.ProviderTimeout, result.ErrorCode);
        Assert.Equal(1, calls);
    }

    // === WaitForOnChainAsync (EVM) ===

    private static JsonElement BuildTxData(string from, string to, string valueHex = "0x16345785D8A0000")
    {
        // 0x16345785D8A0000 = 0.1 ETH in Wei
        var json = $"{{\"from\":\"{from}\",\"to\":\"{to}\",\"value\":\"{valueHex}\",\"nonce\":\"0x5\"}}";
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task WaitForOnChainAsync_ReceiptAppears_ReturnsDetail()
    {
        var calls = 0;
        _provider.GetTransactionReceiptAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                return Task.FromResult<JsonElement?>(calls < 2 ? null : BuildReceipt());
            });
        _provider.GetTransactionByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BuildTxData(TestAddress, "0x000000000000000000000000000000000000dead"));

        var result = await _client.WaitForOnChainAsync("0xabc",
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(10));

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(TransactionStatus.Confirmed, result.Data!.Status);
        Assert.Equal(16L, result.Data.BlockNumber); // 0x10
        await _provider.Received(1).GetTransactionByHashAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WaitForOnChainAsync_RevertedTx_ReturnsOkWithFailedStatus()
    {
        _provider.GetTransactionReceiptAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BuildReceipt(status: "0x0"));
        _provider.GetTransactionByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BuildTxData(TestAddress, "0x000000000000000000000000000000000000dead"));

        var result = await _client.WaitForOnChainAsync("0xR",
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(10));

        Assert.True(result.Success);
        Assert.Equal(TransactionStatus.Failed, result.Data!.Status);
    }

    [Fact]
    public async Task WaitForOnChainAsync_TimeoutBubblesUp()
    {
        _provider.GetTransactionReceiptAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((JsonElement?)null);

        var result = await _client.WaitForOnChainAsync("0xT",
            timeout: TimeSpan.FromMilliseconds(50),
            pollInterval: TimeSpan.FromMilliseconds(10));

        Assert.False(result.Success);
        Assert.Equal(EvmErrorCode.ProviderTimeout, result.ErrorCode);
        await _provider.DidNotReceive().GetTransactionByHashAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WaitForOnChainAsync_BadTxHash_ReturnsInvalidArgument()
    {
        var result = await _client.WaitForOnChainAsync("");
        Assert.False(result.Success);
        Assert.Equal(EvmErrorCode.InvalidArgument, result.ErrorCode);
    }
}
