using ChainKit.Core.Crypto;
using ChainKit.Core.Extensions;
using Xunit;

namespace ChainKit.Core.Tests.Crypto;

public class Keccak256Tests
{
    [Fact]
    public void Hash_EmptyInput_KnownVector()
    {
        var result = Keccak256.Hash(Array.Empty<byte>());
        Assert.Equal("c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470", result.ToHex());
    }

    [Fact]
    public void Hash_TransferSelector_KnownVector()
    {
        // Keccak256("transfer(address,uint256)") -> first 4 bytes = a9059cbb
        var input = System.Text.Encoding.UTF8.GetBytes("transfer(address,uint256)");
        var result = Keccak256.Hash(input);
        Assert.Equal("a9059cbb", result[..4].ToHex());
    }

    [Fact]
    public void Hash_AlwaysReturns32Bytes()
    {
        var result = Keccak256.Hash(new byte[] { 0x41 });
        Assert.Equal(32, result.Length);
    }
}
