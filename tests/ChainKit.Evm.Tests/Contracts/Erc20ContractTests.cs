using System.Numerics;
using System.Text;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ChainKit.Core.Crypto;
using ChainKit.Core.Extensions;
using ChainKit.Evm.Contracts;
using ChainKit.Evm.Crypto;
using ChainKit.Evm.Models;
using ChainKit.Evm.Providers;
using Xunit;

namespace ChainKit.Evm.Tests.Contracts;

public class Erc20ContractTests
{
    private readonly IEvmProvider _provider = Substitute.For<IEvmProvider>();
    private readonly EvmNetworkConfig _network = EvmNetwork.Sepolia;
    private readonly Erc20Contract _contract;
    private const string ContractAddr = "0xdAC17F958D2ee523a2206206994597C13D831ec7";

    public Erc20ContractTests()
    {
        _contract = new Erc20Contract(_provider, ContractAddr, _network);
        // Setup decimals to return 6 by default
        var decimalsSelector = AbiEncoder.EncodeFunctionSelector("decimals()");
        _provider.CallAsync(ContractAddr, Arg.Is<byte[]>(b => b.Length == 4 && b.SequenceEqual(decimalsSelector)), Arg.Any<CancellationToken>())
            .Returns("0x" + AbiEncoder.EncodeUint256(new BigInteger(6)).ToHex());
    }

    [Fact]
    public async Task NameAsync_Success()
    {
        var nameSelector = AbiEncoder.EncodeFunctionSelector("name()");
        _provider.CallAsync(ContractAddr, Arg.Is<byte[]>(b => b.SequenceEqual(nameSelector)), Arg.Any<CancellationToken>())
            .Returns("0x" + EncodeAbiString("Tether USD"));
        var result = await _contract.NameAsync();
        Assert.True(result.Success);
        Assert.Equal("Tether USD", result.Data);
    }

    [Fact]
    public async Task SymbolAsync_Success()
    {
        var symbolSelector = AbiEncoder.EncodeFunctionSelector("symbol()");
        _provider.CallAsync(ContractAddr, Arg.Is<byte[]>(b => b.SequenceEqual(symbolSelector)), Arg.Any<CancellationToken>())
            .Returns("0x" + EncodeAbiString("USDT"));
        var result = await _contract.SymbolAsync();
        Assert.True(result.Success);
        Assert.Equal("USDT", result.Data);
    }

    [Fact]
    public async Task BalanceOfAsync_Success()
    {
        var balData = EvmAbiEncoder.EncodeBalanceOf("0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf");
        _provider.CallAsync(ContractAddr, Arg.Is<byte[]>(b => b.SequenceEqual(balData)), Arg.Any<CancellationToken>())
            .Returns("0x" + AbiEncoder.EncodeUint256(new BigInteger(1_000_000)).ToHex());
        var result = await _contract.BalanceOfAsync("0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf");
        Assert.True(result.Success);
        Assert.Equal(new BigInteger(1_000_000), result.Data);
    }

    [Fact]
    public async Task NameAsync_ProviderThrows_ReturnsFail()
    {
        var nameSelector = AbiEncoder.EncodeFunctionSelector("name()");
        _provider.CallAsync(ContractAddr, Arg.Is<byte[]>(b => b.SequenceEqual(nameSelector)), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("node error"));
        var result = await _contract.NameAsync();
        Assert.False(result.Success);
        Assert.Contains("node error", result.Error!.Message);
    }

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Erc20Contract(null!, ContractAddr, _network));
    }

    // Helper to create ABI-encoded string (offset + length + data)
    private static string EncodeAbiString(string value)
    {
        var strBytes = Encoding.UTF8.GetBytes(value);
        var paddedLen = ((strBytes.Length + 31) / 32) * 32;
        var result = new byte[32 + 32 + paddedLen];
        result[31] = 0x20;
        var lenBytes = AbiEncoder.EncodeUint256(new BigInteger(strBytes.Length));
        Buffer.BlockCopy(lenBytes, 0, result, 32, 32);
        Buffer.BlockCopy(strBytes, 0, result, 64, strBytes.Length);
        return result.ToHex();
    }
}
