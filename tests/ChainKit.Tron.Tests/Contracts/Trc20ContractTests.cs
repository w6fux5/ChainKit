using System.Numerics;
using System.Text;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ChainKit.Core.Extensions;
using ChainKit.Tron.Contracts;
using ChainKit.Tron.Crypto;
using ChainKit.Tron.Models;
using ChainKit.Tron.Protocol.Protobuf;
using ChainKit.Tron.Providers;
using Xunit;

namespace ChainKit.Tron.Tests.Contracts;

public class Trc20ContractTests
{
    private readonly ITronProvider _provider = Substitute.For<ITronProvider>();
    private readonly TronAccount _account;
    private readonly Trc20Contract _contract;

    private const string ContractAddr = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c"; // hex
    private const byte TokenDecimals = 6; // USDT-like

    private static readonly byte[] TestPrivateKey =
        "0000000000000000000000000000000000000000000000000000000000000001".FromHex();

    public Trc20ContractTests()
    {
        _account = TronAccount.FromPrivateKey(TestPrivateKey);
        _contract = new Trc20Contract(_provider, ContractAddr, _account);

        // Set up decimals() to return 6 by default (used by many tests)
        SetupDecimalsReturn(TokenDecimals);
    }

    // === Constructor ===

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Trc20Contract(null!, ContractAddr, _account));
    }

    [Fact]
    public void Constructor_NullContractAddress_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Trc20Contract(_provider, null!, _account));
    }

    [Fact]
    public void Constructor_NullOwnerAccount_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Trc20Contract(_provider, ContractAddr, null!));
    }

    [Fact]
    public void ContractAddress_ReturnsProvidedValue()
    {
        Assert.Equal(ContractAddr, _contract.ContractAddress);
    }

    // === NameAsync ===

    [Fact]
    public async Task NameAsync_ReturnsTokenName()
    {
        var encoded = EncodeString("Tether USD");
        _provider.TriggerConstantContractAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is("name()"), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(encoded);

        var result = await _contract.NameAsync();

        Assert.True(result.Success);
        Assert.Equal("Tether USD", result.Data);
    }

    [Fact]
    public async Task NameAsync_ProviderThrows_ReturnsFail()
    {
        _provider.TriggerConstantContractAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is("name()"), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("timeout"));

        var result = await _contract.NameAsync();

        Assert.False(result.Success);
        Assert.Contains("timeout", result.Error!.Message);
    }

    // === SymbolAsync ===

    [Fact]
    public async Task SymbolAsync_ReturnsSymbol()
    {
        var encoded = EncodeString("USDT");
        _provider.TriggerConstantContractAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is("symbol()"), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(encoded);

        var result = await _contract.SymbolAsync();

        Assert.True(result.Success);
        Assert.Equal("USDT", result.Data);
    }

    // === DecimalsAsync ===

    [Fact]
    public async Task DecimalsAsync_ReturnsDecimals()
    {
        var result = await _contract.DecimalsAsync();

        Assert.True(result.Success);
        Assert.Equal(TokenDecimals, result.Data);
    }

    [Fact]
    public async Task DecimalsAsync_CachesResult()
    {
        // Call twice
        await _contract.DecimalsAsync();
        await _contract.DecimalsAsync();

        // Should only call provider once (second call uses cache)
        await _provider.Received(1).TriggerConstantContractAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is("decimals()"), Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    // === TotalSupplyAsync ===

    [Fact]
    public async Task TotalSupplyAsync_ReturnsConvertedAmount()
    {
        // 1,000,000 USDT = 1_000_000_000_000 raw (6 decimals)
        var rawSupply = new BigInteger(1_000_000_000_000);
        var encoded = EncodeUint256(rawSupply);

        _provider.TriggerConstantContractAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is("totalSupply()"), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(encoded);

        var result = await _contract.TotalSupplyAsync();

        Assert.True(result.Success);
        Assert.Equal(1_000_000m, result.Data);
    }

    // === BalanceOfAsync ===

    [Fact]
    public async Task BalanceOfAsync_ReturnsDecimalAmount()
    {
        // 1 USDT = 1_000_000 raw units (6 decimals)
        var rawBalance = new BigInteger(1_000_000);
        var encoded = EncodeUint256(rawBalance);

        _provider.TriggerConstantContractAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is("balanceOf(address)"), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(encoded);

        var result = await _contract.BalanceOfAsync(_account.HexAddress);

        Assert.True(result.Success);
        Assert.Equal(1m, result.Data);
    }

    [Fact]
    public async Task BalanceOfAsync_ZeroBalance_ReturnsZero()
    {
        var encoded = EncodeUint256(BigInteger.Zero);

        _provider.TriggerConstantContractAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is("balanceOf(address)"), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(encoded);

        var result = await _contract.BalanceOfAsync(_account.HexAddress);

        Assert.True(result.Success);
        Assert.Equal(0m, result.Data);
    }

    [Fact]
    public async Task BalanceOfAsync_ProviderThrows_ReturnsFail()
    {
        _provider.TriggerConstantContractAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is("balanceOf(address)"), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        var result = await _contract.BalanceOfAsync(_account.HexAddress);

        Assert.False(result.Success);
        Assert.Contains("Network error", result.Error!.Message);
    }

    // === AllowanceAsync ===

    [Fact]
    public async Task AllowanceAsync_ReturnsAllowance()
    {
        // 500 USDT = 500_000_000 raw units
        var rawAllowance = new BigInteger(500_000_000);
        var encoded = EncodeUint256(rawAllowance);

        _provider.TriggerConstantContractAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is("allowance(address,address)"), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(encoded);

        var result = await _contract.AllowanceAsync(_account.HexAddress, ContractAddr);

        Assert.True(result.Success);
        Assert.Equal(500m, result.Data);
    }

    // === TransferAsync ===

    [Fact]
    public async Task TransferAsync_Success_ReturnsTxId()
    {
        SetupWriteSuccess("transfer_tx_123");

        var result = await _contract.TransferAsync(_account.HexAddress, 100m);

        Assert.True(result.Success);
        Assert.Equal("transfer_tx_123", result.Data!.TxId);
        Assert.Equal(100m, result.Data.Amount);
    }

    [Fact]
    public async Task TransferAsync_BroadcastFails_ReturnsFail()
    {
        SetupWriteFailure("CONTRACT_REVERT");

        var result = await _contract.TransferAsync(_account.HexAddress, 100m);

        Assert.False(result.Success);
        Assert.Contains("CONTRACT_REVERT", result.Error!.Message);
    }

    [Fact]
    public async Task TransferAsync_ProviderThrows_ReturnsFail()
    {
        _provider.TriggerSmartContractAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<byte[]>(),
                Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _contract.TransferAsync(_account.HexAddress, 50m);

        Assert.False(result.Success);
        Assert.Contains("Connection refused", result.Error!.Message);
    }

    // === ApproveAsync ===

    [Fact]
    public async Task ApproveAsync_Success_ReturnsTxId()
    {
        SetupWriteSuccess("approve_tx_456");

        var result = await _contract.ApproveAsync(_account.HexAddress, 200m);

        Assert.True(result.Success);
        Assert.Equal("approve_tx_456", result.Data!.TxId);
        Assert.Equal(200m, result.Data.Amount);
    }

    // === MintAsync ===

    [Fact]
    public async Task MintAsync_Success_ReturnsTxId()
    {
        SetupWriteSuccess("mint_tx_789");

        var result = await _contract.MintAsync(_account.HexAddress, 1000m);

        Assert.True(result.Success);
        Assert.Equal("mint_tx_789", result.Data!.TxId);
        Assert.Equal(1000m, result.Data.Amount);
    }

    // === BurnAsync ===

    [Fact]
    public async Task BurnAsync_Success_ReturnsTxId()
    {
        SetupWriteSuccess("burn_tx_abc");

        var result = await _contract.BurnAsync(50m);

        Assert.True(result.Success);
        Assert.Equal("burn_tx_abc", result.Data!.TxId);
        Assert.Equal(50m, result.Data.Amount);
    }

    // === BurnFromAsync ===

    [Fact]
    public async Task BurnFromAsync_Success_ReturnsTxId()
    {
        SetupWriteSuccess("burnfrom_tx_def");

        var result = await _contract.BurnFromAsync(_account.HexAddress, 25m);

        Assert.True(result.Success);
        Assert.Equal("burnfrom_tx_def", result.Data!.TxId);
        Assert.Equal(25m, result.Data.Amount);
    }

    // === Helpers ===

    private void SetupDecimalsReturn(byte decimals)
    {
        var encoded = EncodeUint256(new BigInteger(decimals));
        _provider.TriggerConstantContractAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is("decimals()"), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(encoded);
    }

    private void SetupWriteSuccess(string txId)
    {
        // Return a minimal valid unsigned transaction from TriggerSmartContract
        var tx = CreateMinimalTransaction();
        _provider.TriggerSmartContractAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<byte[]>(),
                Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(tx);

        _provider.BroadcastTransactionAsync(
                Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns(new BroadcastResult(true, txId, null));
    }

    private void SetupWriteFailure(string errorMessage)
    {
        var tx = CreateMinimalTransaction();
        _provider.TriggerSmartContractAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<byte[]>(),
                Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(tx);

        _provider.BroadcastTransactionAsync(
                Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns(new BroadcastResult(false, null, errorMessage));
    }

    private static Transaction CreateMinimalTransaction()
    {
        var tx = new Transaction
        {
            RawData = new Transaction.Types.raw
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Expiration = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
                RefBlockBytes = Google.Protobuf.ByteString.CopyFrom(new byte[2]),
                RefBlockHash = Google.Protobuf.ByteString.CopyFrom(new byte[8])
            }
        };

        var trigger = new TriggerSmartContract
        {
            OwnerAddress = Google.Protobuf.ByteString.CopyFrom(new byte[21]),
            ContractAddress = Google.Protobuf.ByteString.CopyFrom(new byte[21]),
            Data = Google.Protobuf.ByteString.CopyFrom(new byte[4])
        };

        tx.RawData.Contract.Add(new Transaction.Types.Contract
        {
            Type = Transaction.Types.Contract.Types.ContractType.TriggerSmartContract,
            Parameter = Google.Protobuf.WellKnownTypes.Any.Pack(trigger, "type.googleapis.com")
        });

        return tx;
    }

    /// <summary>
    /// ABI-encodes a string value (offset + length + padded data).
    /// </summary>
    private static byte[] EncodeString(string value)
    {
        var strBytes = Encoding.UTF8.GetBytes(value);
        var paddedLen = ((strBytes.Length + 31) / 32) * 32;

        // offset (32 bytes) + length (32 bytes) + padded string data
        var result = new byte[32 + 32 + paddedLen];

        // Offset: points to byte 32 (0x20)
        result[31] = 0x20;

        // Length
        var lenBytes = new BigInteger(strBytes.Length).ToByteArray(isUnsigned: true, isBigEndian: true);
        Buffer.BlockCopy(lenBytes, 0, result, 64 - lenBytes.Length, lenBytes.Length);

        // String data
        Buffer.BlockCopy(strBytes, 0, result, 64, strBytes.Length);

        return result;
    }

    /// <summary>
    /// ABI-encodes a uint256 value (32-byte big-endian, zero-padded).
    /// </summary>
    private static byte[] EncodeUint256(BigInteger value)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        var padded = new byte[32];
        Buffer.BlockCopy(bytes, 0, padded, 32 - bytes.Length, bytes.Length);
        return padded;
    }
}
