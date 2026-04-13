using ChainKit.Core.Crypto;
using ChainKit.Core.Extensions;
using ChainKit.Evm.Crypto;
using Xunit;

namespace ChainKit.Evm.Tests.Crypto;

public class EvmSignerTests
{
    private static readonly byte[] TestPrivateKey =
        "0000000000000000000000000000000000000000000000000000000000000001".FromHex();

    [Fact]
    public void SignTyped_Returns65Bytes()
    {
        var hash = Keccak256.Hash(new byte[] { 0x01, 0x02, 0x03 });
        var sig = EvmSigner.SignTyped(hash, TestPrivateKey);
        Assert.Equal(65, sig.Length);
    }

    [Fact]
    public void SignTyped_RecoveryIdIsZeroOrOne()
    {
        var hash = Keccak256.Hash(new byte[] { 0x01 });
        var sig = EvmSigner.SignTyped(hash, TestPrivateKey);
        Assert.True(sig[64] == 0 || sig[64] == 1);
    }

    [Fact]
    public void SignLegacy_ReturnsRawRecoveryId()
    {
        var hash = Keccak256.Hash(new byte[] { 0x01 });
        var sig = EvmSigner.SignLegacy(hash, TestPrivateKey);
        Assert.True(sig[64] == 0 || sig[64] == 1, $"Expected recId=0 or 1, got {sig[64]}");
    }

    [Fact]
    public void SignLegacy_SameResultAsSignTyped()
    {
        var hash = Keccak256.Hash(new byte[] { 0x01 });
        var sigLegacy = EvmSigner.SignLegacy(hash, TestPrivateKey);
        var sigTyped = EvmSigner.SignTyped(hash, TestPrivateKey);
        Assert.Equal(sigTyped, sigLegacy);
    }

    [Fact]
    public void Verify_ValidSignature_ReturnsTrue()
    {
        var ecKey = NBitcoin.Secp256k1.ECPrivKey.Create(TestPrivateKey);
        var pubKey = ecKey.CreatePubKey();
        var compressed = new byte[33];
        pubKey.WriteToSpan(true, compressed, out _);
        var hash = Keccak256.Hash(new byte[] { 0x42 });
        var sig = EvmSigner.SignTyped(hash, TestPrivateKey);
        Assert.True(EvmSigner.Verify(hash, sig, compressed));
    }

    [Fact]
    public void Verify_WrongData_ReturnsFalse()
    {
        var ecKey = NBitcoin.Secp256k1.ECPrivKey.Create(TestPrivateKey);
        var pubKey = ecKey.CreatePubKey();
        var compressed = new byte[33];
        pubKey.WriteToSpan(true, compressed, out _);
        var hash = Keccak256.Hash(new byte[] { 0x42 });
        var wrongHash = Keccak256.Hash(new byte[] { 0x43 });
        var sig = EvmSigner.SignTyped(hash, TestPrivateKey);
        Assert.False(EvmSigner.Verify(wrongHash, sig, compressed));
    }
}
