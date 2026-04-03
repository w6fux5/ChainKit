using ChainKit.Core.Extensions;
using ChainKit.Tron.Crypto;
using Xunit;

namespace ChainKit.Tron.Tests.Crypto;

public class TronAccountTests
{
    [Fact]
    public void Create_GeneratesValidAccount()
    {
        var account = TronAccount.Create();
        Assert.StartsWith("T", account.Address);
        Assert.StartsWith("41", account.HexAddress);
        Assert.Equal(32, account.PrivateKey.Length);
        Assert.NotEmpty(account.PublicKey);
        Assert.True(TronAddress.IsValid(account.Address));
    }

    [Fact]
    public void Create_TwoCalls_DifferentAccounts()
    {
        Assert.NotEqual(TronAccount.Create().Address, TronAccount.Create().Address);
    }

    [Fact]
    public void FromPrivateKey_KnownKey_ProducesValidAddress()
    {
        var key = "0000000000000000000000000000000000000000000000000000000000000001".FromHex();
        var account = TronAccount.FromPrivateKey(key);
        Assert.True(TronAddress.IsValid(account.Address));
        Assert.StartsWith("T", account.Address);
    }

    [Fact]
    public void FromPrivateKey_SameKey_SameAccount()
    {
        var key = "0000000000000000000000000000000000000000000000000000000000000001".FromHex();
        Assert.Equal(TronAccount.FromPrivateKey(key).Address, TronAccount.FromPrivateKey(key).Address);
    }

    [Fact]
    public void FromMnemonic_SameMnemonic_SameAccount()
    {
        var m = Mnemonic.Generate(12);
        Assert.Equal(TronAccount.FromMnemonic(m, 0).Address, TronAccount.FromMnemonic(m, 0).Address);
    }

    [Fact]
    public void FromMnemonic_DifferentIndex_DifferentAccount()
    {
        var m = Mnemonic.Generate(12);
        Assert.NotEqual(TronAccount.FromMnemonic(m, 0).Address, TronAccount.FromMnemonic(m, 1).Address);
    }
}
