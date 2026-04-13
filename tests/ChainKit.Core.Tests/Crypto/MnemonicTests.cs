using ChainKit.Core.Crypto;
using Xunit;

namespace ChainKit.Core.Tests.Crypto;

public class MnemonicTests
{
    [Fact]
    public void Generate_12Words_Returns12Words()
    {
        var mnemonic = Mnemonic.Generate(12);
        Assert.Equal(12, mnemonic.Split(' ').Length);
    }

    [Fact]
    public void Generate_TwoCalls_DifferentResults()
    {
        Assert.NotEqual(Mnemonic.Generate(12), Mnemonic.Generate(12));
    }

    [Fact]
    public void Validate_ValidMnemonic_ReturnsTrue()
    {
        Assert.True(Mnemonic.Validate(Mnemonic.Generate(12)));
    }

    [Fact]
    public void Validate_InvalidMnemonic_ReturnsFalse()
    {
        Assert.False(Mnemonic.Validate("invalid words that are not a real mnemonic phrase at all ever"));
    }

    [Fact]
    public void ToSeed_SameMnemonic_SameSeed()
    {
        var m = Mnemonic.Generate(12);
        Assert.Equal(Mnemonic.ToSeed(m), Mnemonic.ToSeed(m));
    }

    [Fact]
    public void ToSeed_Returns64Bytes()
    {
        Assert.Equal(64, Mnemonic.ToSeed(Mnemonic.Generate(12)).Length);
    }
}
