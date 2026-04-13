using ChainKit.Core.Extensions;
using ChainKit.Evm.Protocol;
using Xunit;

namespace ChainKit.Evm.Tests.Protocol;

public class RlpEncoderTests
{
    [Fact]
    public void EncodeElement_EmptyBytes_Returns0x80()
    {
        var result = RlpEncoder.EncodeElement(Array.Empty<byte>());
        Assert.Equal("80", result.ToHex());
    }

    [Fact]
    public void EncodeElement_SingleByteLessThan0x80_ReturnsByteSelf()
    {
        var result = RlpEncoder.EncodeElement(new byte[] { 0x0f });
        Assert.Equal("0f", result.ToHex());
    }

    [Fact]
    public void EncodeElement_SingleByte0x80_Returns8180()
    {
        var result = RlpEncoder.EncodeElement(new byte[] { 0x80 });
        Assert.Equal("8180", result.ToHex());
    }

    [Fact]
    public void EncodeElement_ShortString_Dog()
    {
        var result = RlpEncoder.EncodeElement(System.Text.Encoding.ASCII.GetBytes("dog"));
        Assert.Equal("83646f67", result.ToHex());
    }

    [Fact]
    public void EncodeElement_55Bytes_ShortStringPrefix()
    {
        var data = new byte[55];
        Array.Fill(data, (byte)0xAA);
        var result = RlpEncoder.EncodeElement(data);
        Assert.Equal(0x80 + 55, result[0]);
        Assert.Equal(56, result.Length);
    }

    [Fact]
    public void EncodeElement_56Bytes_LongStringPrefix()
    {
        var data = new byte[56];
        Array.Fill(data, (byte)0xBB);
        var result = RlpEncoder.EncodeElement(data);
        Assert.Equal(0xb8, result[0]);
        Assert.Equal(56, result[1]);
        Assert.Equal(58, result.Length);
    }

    [Fact]
    public void EncodeList_Empty_Returns0xC0()
    {
        var result = RlpEncoder.EncodeList();
        Assert.Equal("c0", result.ToHex());
    }

    [Fact]
    public void EncodeList_CatDog()
    {
        var cat = RlpEncoder.EncodeElement(System.Text.Encoding.ASCII.GetBytes("cat"));
        var dog = RlpEncoder.EncodeElement(System.Text.Encoding.ASCII.GetBytes("dog"));
        var result = RlpEncoder.EncodeList(cat, dog);
        Assert.Equal("c88363617483646f67", result.ToHex());
    }

    [Fact]
    public void EncodeElement_Integer15_Returns0x0f()
    {
        var result = RlpEncoder.EncodeElement(new byte[] { 0x0f });
        Assert.Equal("0f", result.ToHex());
    }

    [Fact]
    public void EncodeElement_Integer1024_Returns820400()
    {
        var result = RlpEncoder.EncodeElement(new byte[] { 0x04, 0x00 });
        Assert.Equal("820400", result.ToHex());
    }

    [Fact]
    public void EncodeList_Nested_SetTheoreticRepresentation()
    {
        // [ [], [[]], [ [], [[]] ] ] per Ethereum spec
        var empty = RlpEncoder.EncodeList();
        var innerNested = RlpEncoder.EncodeList(empty);
        var last = RlpEncoder.EncodeList(empty, innerNested);
        var result = RlpEncoder.EncodeList(empty, innerNested, last);
        Assert.Equal("c7c0c1c0c3c0c1c0", result.ToHex());
    }

    [Fact]
    public void EncodeLong_Zero_ReturnsEncodedEmpty()
    {
        var result = RlpEncoder.EncodeLong(0);
        Assert.Equal("80", result.ToHex());
    }

    [Fact]
    public void EncodeLong_SmallValue_Returns0x0f()
    {
        var result = RlpEncoder.EncodeLong(15);
        Assert.Equal("0f", result.ToHex());
    }

    [Fact]
    public void EncodeUint_LargeValue()
    {
        var result = RlpEncoder.EncodeUint(new System.Numerics.BigInteger(256));
        Assert.Equal("820100", result.ToHex());
    }
}
