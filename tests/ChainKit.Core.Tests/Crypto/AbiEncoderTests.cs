using System.Numerics;
using System.Text;
using ChainKit.Core.Crypto;
using ChainKit.Core.Extensions;
using Xunit;

namespace ChainKit.Core.Tests.Crypto;

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
    public void EncodeUint256_SmallValue()
    {
        var encoded = AbiEncoder.EncodeUint256(new BigInteger(1));
        Assert.Equal(32, encoded.Length);
        Assert.Equal("0000000000000000000000000000000000000000000000000000000000000001", encoded.ToHex());
    }

    [Fact]
    public void EncodeUint256_LargerValue()
    {
        var encoded = AbiEncoder.EncodeUint256(new BigInteger(1000000));
        Assert.Equal(32, encoded.Length);
        Assert.Equal("00000000000000000000000000000000000000000000000000000000000f4240", encoded.ToHex());
    }

    [Fact]
    public void DecodeUint256_Roundtrip()
    {
        var original = new BigInteger(123456789);
        var encoded = AbiEncoder.EncodeUint256(original);
        Assert.Equal(original, AbiEncoder.DecodeUint256(encoded));
    }

    [Fact]
    public void DecodeString_ValidData()
    {
        // Manually build ABI-encoded string for "USDT"
        var str = "USDT";
        var strBytes = Encoding.UTF8.GetBytes(str);

        var data = new byte[96]; // offset(32) + length(32) + padded data(32)
        // offset slot: points to position 32 (0x20)
        AbiEncoder.EncodeUint256(new BigInteger(32)).CopyTo(data, 0);
        // length slot
        AbiEncoder.EncodeUint256(new BigInteger(strBytes.Length)).CopyTo(data, 32);
        // data
        Buffer.BlockCopy(strBytes, 0, data, 64, strBytes.Length);

        Assert.Equal("USDT", AbiEncoder.DecodeString(data));
    }

    [Fact]
    public void DecodeString_TooShort_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, AbiEncoder.DecodeString(new byte[32]));
    }
}
