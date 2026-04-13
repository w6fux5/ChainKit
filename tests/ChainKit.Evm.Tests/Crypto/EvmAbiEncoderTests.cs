using System.Numerics;
using ChainKit.Core.Extensions;
using ChainKit.Evm.Crypto;
using Xunit;

namespace ChainKit.Evm.Tests.Crypto;

public class EvmAbiEncoderTests
{
    [Fact]
    public void EncodeAddress_0xPrefix_PadsTo32Bytes()
    {
        var encoded = EvmAbiEncoder.EncodeAddress("0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045");
        Assert.Equal(32, encoded.Length);
        Assert.Equal(new byte[12], encoded[..12]);
    }

    [Fact]
    public void DecodeAddress_Returns0xChecksumAddress()
    {
        var address = "0xd8da6bf26964af9d7eed9e03e53415d37aa96045";
        var encoded = EvmAbiEncoder.EncodeAddress(address);
        var decoded = EvmAbiEncoder.DecodeAddress(encoded);
        Assert.StartsWith("0x", decoded);
        Assert.Equal(42, decoded.Length);
    }

    [Fact]
    public void EncodeTransfer_CorrectSelectorAndLength()
    {
        var encoded = EvmAbiEncoder.EncodeTransfer("0xd8da6bf26964af9d7eed9e03e53415d37aa96045", new BigInteger(1000000));
        Assert.Equal(4 + 32 + 32, encoded.Length);
        Assert.Equal("a9059cbb", encoded[..4].ToHex());
    }

    [Fact]
    public void EncodeBalanceOf_CorrectSelector()
    {
        var encoded = EvmAbiEncoder.EncodeBalanceOf("0xd8da6bf26964af9d7eed9e03e53415d37aa96045");
        Assert.Equal(4 + 32, encoded.Length);
        Assert.Equal("70a08231", encoded[..4].ToHex());
    }

    [Fact]
    public void EncodeApprove_CorrectSelector()
    {
        var encoded = EvmAbiEncoder.EncodeApprove("0xd8da6bf26964af9d7eed9e03e53415d37aa96045", BigInteger.One);
        Assert.Equal(4 + 32 + 32, encoded.Length);
        Assert.Equal("095ea7b3", encoded[..4].ToHex());
    }
}
