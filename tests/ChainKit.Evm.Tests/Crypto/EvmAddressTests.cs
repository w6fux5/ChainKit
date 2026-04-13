using ChainKit.Core.Extensions;
using ChainKit.Evm.Crypto;
using Xunit;

namespace ChainKit.Evm.Tests.Crypto;

public class EvmAddressTests
{
    [Theory]
    [InlineData("0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045", true)]
    [InlineData("0xd8da6bf26964af9d7eed9e03e53415d37aa96045", true)]
    [InlineData("0xD8DA6BF26964AF9D7EED9E03E53415D37AA96045", true)]
    [InlineData("0x123", false)]
    [InlineData("not an address", false)]
    [InlineData("", false)]
    public void IsValid_VariousCases(string address, bool expected)
    {
        Assert.Equal(expected, EvmAddress.IsValid(address));
    }

    [Fact]
    public void ToChecksumAddress_EIP55_KnownVector()
    {
        var input = "0xfb6916095ca1df60bb79ce92ce3ea74c37c5d359";
        var expected = "0xfB6916095ca1df60bB79Ce92cE3Ea74c37c5d359";
        Assert.Equal(expected, EvmAddress.ToChecksumAddress(input));
    }

    [Fact]
    public void FromPublicKey_KnownVector()
    {
        var ecKey = NBitcoin.Secp256k1.ECPrivKey.Create(
            "0000000000000000000000000000000000000000000000000000000000000001".FromHex());
        var pubKey = ecKey.CreatePubKey();
        var uncompressed = new byte[65];
        pubKey.WriteToSpan(false, uncompressed, out _);
        var address = EvmAddress.FromPublicKey(uncompressed);
        Assert.Equal("0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf", address);
    }
}
