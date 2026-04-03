using System.Numerics;
using ChainKit.Core.Extensions;
using ChainKit.Tron.Crypto;
using Xunit;

namespace ChainKit.Tron.Tests.Crypto;

public class AbiEncoderTests
{
    [Fact]
    public void EncodeFunctionSelector_Transfer()
    {
        var selector = AbiEncoder.EncodeFunctionSelector("transfer(address,uint256)");
        Assert.Equal("a9059cbb", selector.ToHex());
    }

    [Fact]
    public void EncodeFunctionSelector_BalanceOf()
    {
        var selector = AbiEncoder.EncodeFunctionSelector("balanceOf(address)");
        Assert.Equal("70a08231", selector.ToHex());
    }

    [Fact]
    public void EncodeFunctionSelector_Approve()
    {
        var selector = AbiEncoder.EncodeFunctionSelector("approve(address,uint256)");
        Assert.Equal("095ea7b3", selector.ToHex());
    }

    [Fact]
    public void EncodeAddress_PadsTo32Bytes()
    {
        var address = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
        var encoded = AbiEncoder.EncodeAddress(address);
        Assert.Equal(32, encoded.Length);
        // Should strip 41 prefix, then left-pad with zeros
        Assert.Equal("000000000000000000000000a614f803b6fd780986a42c78ec9c7f77e6ded13c", encoded.ToHex());
    }

    [Fact]
    public void EncodeUint256_SmallValue()
    {
        var encoded = AbiEncoder.EncodeUint256(new BigInteger(1000000));
        Assert.Equal(32, encoded.Length);
        Assert.Equal("00000000000000000000000000000000000000000000000000000000000f4240", encoded.ToHex());
    }

    [Fact]
    public void EncodeTransfer_CombinesSelectorAndParams()
    {
        var to = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
        var amount = new BigInteger(1000000);
        var encoded = AbiEncoder.EncodeTransfer(to, amount);
        Assert.Equal(4 + 32 + 32, encoded.Length);
        Assert.Equal("a9059cbb", encoded[..4].ToHex());
    }

    [Fact]
    public void EncodeBalanceOf_CombinesSelectorAndAddress()
    {
        var address = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
        var encoded = AbiEncoder.EncodeBalanceOf(address);
        Assert.Equal(4 + 32, encoded.Length);
        Assert.Equal("70a08231", encoded[..4].ToHex());
    }

    [Fact]
    public void DecodeUint256_Roundtrip()
    {
        var original = new BigInteger(123456789);
        var encoded = AbiEncoder.EncodeUint256(original);
        Assert.Equal(original, AbiEncoder.DecodeUint256(encoded));
    }

    [Fact]
    public void DecodeAddress_Roundtrip()
    {
        var original = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
        var encoded = AbiEncoder.EncodeAddress(original);
        Assert.Equal(original, AbiEncoder.DecodeAddress(encoded));
    }
}
