using System.Numerics;
using ChainKit.Core.Crypto;
using ChainKit.Core.Extensions;
using ChainKit.Tron.Crypto;
using Xunit;

namespace ChainKit.Tron.Tests.Crypto;

public class TronAbiEncoderTests
{
    [Fact]
    public void EncodeAddress_PadsTo32Bytes()
    {
        var address = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
        var encoded = TronAbiEncoder.EncodeAddress(address);
        Assert.Equal(32, encoded.Length);
        // Should strip 41 prefix, then left-pad with zeros
        Assert.Equal("000000000000000000000000a614f803b6fd780986a42c78ec9c7f77e6ded13c", encoded.ToHex());
    }

    [Fact]
    public void EncodeTransfer_CombinesSelectorAndParams()
    {
        var to = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
        var amount = new BigInteger(1000000);
        var encoded = TronAbiEncoder.EncodeTransfer(to, amount);
        Assert.Equal(4 + 32 + 32, encoded.Length);
        Assert.Equal("a9059cbb", encoded[..4].ToHex());
    }

    [Fact]
    public void EncodeBalanceOf_CombinesSelectorAndAddress()
    {
        var address = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
        var encoded = TronAbiEncoder.EncodeBalanceOf(address);
        Assert.Equal(4 + 32, encoded.Length);
        Assert.Equal("70a08231", encoded[..4].ToHex());
    }

    [Fact]
    public void DecodeAddress_Roundtrip()
    {
        var original = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
        var encoded = TronAbiEncoder.EncodeAddress(original);
        Assert.Equal(original, TronAbiEncoder.DecodeAddress(encoded));
    }

    [Fact]
    public void EncodeApprove_CombinesSelectorSpenderAndAmount()
    {
        var spender = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
        var amount = new BigInteger(500000);
        var encoded = TronAbiEncoder.EncodeApprove(spender, amount);
        Assert.Equal(4 + 32 + 32, encoded.Length);
        Assert.Equal("095ea7b3", encoded[..4].ToHex());
    }

    [Fact]
    public void EncodeMint_CombinesSelectorAddressAndAmount()
    {
        var to = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
        var amount = new BigInteger(1000000);
        var encoded = TronAbiEncoder.EncodeMint(to, amount);
        Assert.Equal(4 + 32 + 32, encoded.Length);
    }

    [Fact]
    public void EncodeBurn_CombinesSelectorAndAmount()
    {
        var amount = new BigInteger(1000000);
        var encoded = TronAbiEncoder.EncodeBurn(amount);
        Assert.Equal(4 + 32, encoded.Length);
    }

    [Fact]
    public void EncodeBurnFrom_CombinesSelectorAddressAndAmount()
    {
        var from = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
        var amount = new BigInteger(1000000);
        var encoded = TronAbiEncoder.EncodeBurnFrom(from, amount);
        Assert.Equal(4 + 32 + 32, encoded.Length);
    }

    [Fact]
    public void EncodeAllowance_CombinesSelectorAndTwoAddresses()
    {
        var owner = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
        var spender = "41b614f803b6fd780986a42c78ec9c7f77e6ded13c";
        var encoded = TronAbiEncoder.EncodeAllowance(owner, spender);
        Assert.Equal(4 + 32 + 32, encoded.Length);
    }
}
