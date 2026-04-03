using ChainKit.Core.Extensions;
using Xunit;

namespace ChainKit.Core.Tests;

public class HexExtensionsTests
{
    [Fact]
    public void ToHex_ReturnsLowercaseHexString()
    {
        var bytes = new byte[] { 0x41, 0xAB, 0xCD, 0xEF };
        Assert.Equal("41abcdef", bytes.ToHex());
    }

    [Fact]
    public void ToHex_EmptyArray_ReturnsEmptyString()
    {
        Assert.Equal("", Array.Empty<byte>().ToHex());
    }

    [Fact]
    public void FromHex_ParsesHexString()
    {
        var result = "41abcdef".FromHex();
        Assert.Equal(new byte[] { 0x41, 0xAB, 0xCD, 0xEF }, result);
    }

    [Fact]
    public void FromHex_UpperCase_ParsesCorrectly()
    {
        var result = "41ABCDEF".FromHex();
        Assert.Equal(new byte[] { 0x41, 0xAB, 0xCD, 0xEF }, result);
    }

    [Fact]
    public void FromHex_WithPrefix_ParsesCorrectly()
    {
        var result = "0x41ab".FromHex();
        Assert.Equal(new byte[] { 0x41, 0xAB }, result);
    }

    [Fact]
    public void Roundtrip_PreservesData()
    {
        var original = new byte[] { 0x00, 0xFF, 0x12, 0x34 };
        Assert.Equal(original, original.ToHex().FromHex());
    }
}
