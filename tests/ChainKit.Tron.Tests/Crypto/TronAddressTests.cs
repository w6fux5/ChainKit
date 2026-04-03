using ChainKit.Tron.Crypto;
using Xunit;

namespace ChainKit.Tron.Tests.Crypto;

public class TronAddressTests
{
    private const string KnownHex = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
    private const string KnownBase58 = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";

    [Fact]
    public void ToBase58_KnownVector() => Assert.Equal(KnownBase58, TronAddress.ToBase58(KnownHex));

    [Fact]
    public void ToHex_KnownVector() => Assert.Equal(KnownHex, TronAddress.ToHex(KnownBase58));

    [Fact]
    public void IsValid_ValidBase58_ReturnsTrue() => Assert.True(TronAddress.IsValid(KnownBase58));

    [Fact]
    public void IsValid_ValidHex_ReturnsTrue() => Assert.True(TronAddress.IsValid(KnownHex));

    [Fact]
    public void IsValid_InvalidAddress_ReturnsFalse()
    {
        Assert.False(TronAddress.IsValid("not_an_address"));
        Assert.False(TronAddress.IsValid(""));
        Assert.False(TronAddress.IsValid("T"));
    }

    [Fact]
    public void IsValid_WrongPrefix_ReturnsFalse() =>
        Assert.False(TronAddress.IsValid("42a614f803b6fd780986a42c78ec9c7f77e6ded13c"));

    [Fact]
    public void Roundtrip_Hex_Base58_Hex()
    {
        var base58 = TronAddress.ToBase58(KnownHex);
        var hex = TronAddress.ToHex(base58);
        Assert.Equal(KnownHex, hex);
    }
}
