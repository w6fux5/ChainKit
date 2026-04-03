using ChainKit.Core.Extensions;
using Xunit;

namespace ChainKit.Core.Tests;

public class Base58ExtensionsTests
{
    [Fact]
    public void ToBase58Check_KnownVector()
    {
        var bytes = Convert.FromHexString("41a614f803b6fd780986a42c78ec9c7f77e6ded13c");
        var result = bytes.ToBase58Check();
        Assert.Equal("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", result);
    }

    [Fact]
    public void FromBase58Check_KnownVector()
    {
        var result = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t".FromBase58Check();
        var expected = Convert.FromHexString("41a614f803b6fd780986a42c78ec9c7f77e6ded13c");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Roundtrip_PreservesData()
    {
        var original = Convert.FromHexString("41a614f803b6fd780986a42c78ec9c7f77e6ded13c");
        var encoded = original.ToBase58Check();
        var decoded = encoded.FromBase58Check();
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void FromBase58Check_InvalidChecksum_Throws()
    {
        Assert.Throws<FormatException>(() => "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6X".FromBase58Check());
    }
}
