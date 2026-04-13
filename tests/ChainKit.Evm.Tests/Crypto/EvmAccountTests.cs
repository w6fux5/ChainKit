using ChainKit.Core.Extensions;
using ChainKit.Evm.Crypto;
using Xunit;

namespace ChainKit.Evm.Tests.Crypto;

public class EvmAccountTests
{
    [Fact]
    public void Create_ReturnsValidAccount()
    {
        var account = EvmAccount.Create();
        Assert.True(EvmAddress.IsValid(account.Address));
        Assert.Equal(33, account.PublicKey.Length);
        Assert.Equal(32, account.PrivateKey.Length);
    }

    [Fact]
    public void FromPrivateKey_KnownVector()
    {
        var key = "0000000000000000000000000000000000000000000000000000000000000001".FromHex();
        var account = EvmAccount.FromPrivateKey(key);
        Assert.Equal("0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf", account.Address);
    }

    [Fact]
    public void FromPrivateKey_SameKey_SameAddress()
    {
        var key = "0000000000000000000000000000000000000000000000000000000000000002".FromHex();
        Assert.Equal(EvmAccount.FromPrivateKey(key).Address, EvmAccount.FromPrivateKey(key).Address);
    }

    [Fact]
    public void FromMnemonic_ReturnsValidAccount()
    {
        var mnemonic = ChainKit.Core.Crypto.Mnemonic.Generate(12);
        var account = EvmAccount.FromMnemonic(mnemonic);
        Assert.True(EvmAddress.IsValid(account.Address));
    }

    [Fact]
    public void FromMnemonic_DifferentIndex_DifferentAddress()
    {
        var mnemonic = ChainKit.Core.Crypto.Mnemonic.Generate(12);
        Assert.NotEqual(EvmAccount.FromMnemonic(mnemonic, 0).Address, EvmAccount.FromMnemonic(mnemonic, 1).Address);
    }

    [Fact]
    public void Dispose_ZerosPrivateKey()
    {
        var key = "0000000000000000000000000000000000000000000000000000000000000003".FromHex();
        var account = EvmAccount.FromPrivateKey(key);
        account.Dispose();
        Assert.All(account.PrivateKey, b => Assert.Equal(0, b));
    }

    [Fact]
    public void ImplementsIAccount()
    {
        var account = EvmAccount.Create();
        ChainKit.Core.IAccount iAccount = account;
        Assert.NotNull(iAccount.Address);
        Assert.NotNull(iAccount.PublicKey);
    }
}
