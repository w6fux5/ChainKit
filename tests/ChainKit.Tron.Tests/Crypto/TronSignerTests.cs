using ChainKit.Core.Extensions;
using ChainKit.Tron.Crypto;
using Xunit;

namespace ChainKit.Tron.Tests.Crypto;

public class TronSignerTests
{
    [Fact]
    public void Sign_ReturnsNonEmpty65ByteSignature()
    {
        var privateKey = "0000000000000000000000000000000000000000000000000000000000000001".FromHex();
        var data = new byte[32];
        var signature = TronSigner.Sign(data, privateKey);
        Assert.Equal(65, signature.Length);
    }

    [Fact]
    public void Sign_SameInput_SameOutput()
    {
        var privateKey = "0000000000000000000000000000000000000000000000000000000000000001".FromHex();
        var data = new byte[32];
        Assert.Equal(TronSigner.Sign(data, privateKey), TronSigner.Sign(data, privateKey));
    }

    [Fact]
    public void Sign_DifferentKey_DifferentSignature()
    {
        var key1 = "0000000000000000000000000000000000000000000000000000000000000001".FromHex();
        var key2 = "0000000000000000000000000000000000000000000000000000000000000002".FromHex();
        var data = new byte[32];
        Assert.NotEqual(TronSigner.Sign(data, key1), TronSigner.Sign(data, key2));
    }

    [Fact]
    public void Verify_ValidSignature_ReturnsTrue()
    {
        var privateKey = "0000000000000000000000000000000000000000000000000000000000000001".FromHex();
        var data = new byte[32];

        var ecKey = NBitcoin.Secp256k1.ECPrivKey.Create(privateKey);
        var pubBytes = new byte[33];
        ecKey.CreatePubKey().WriteToSpan(true, pubBytes, out _);

        var signature = TronSigner.Sign(data, privateKey);
        Assert.True(TronSigner.Verify(data, signature, pubBytes));
    }
}
