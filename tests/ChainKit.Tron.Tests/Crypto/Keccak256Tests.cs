using ChainKit.Core.Extensions;
using ChainKit.Tron.Crypto;
using Xunit;

namespace ChainKit.Tron.Tests.Crypto;

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
        // Keccak256("transfer(address,uint256)") → first 4 bytes = a9059cbb
        var input = System.Text.Encoding.UTF8.GetBytes("transfer(address,uint256)");
        var result = Keccak256.Hash(input);
        Assert.Equal("a9059cbb", result[..4].ToHex());
    }

    [Fact]
    public void Hash_BalanceOfSelector()
    {
        var input = System.Text.Encoding.UTF8.GetBytes("balanceOf(address)");
        var result = Keccak256.Hash(input);
        Assert.Equal("70a08231", result[..4].ToHex());
    }

    [Fact]
    public void Hash_DifferentInputs_DifferentOutputs()
    {
        var a = Keccak256.Hash(new byte[] { 0x01 });
        var b = Keccak256.Hash(new byte[] { 0x02 });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Hash_AlwaysReturns32Bytes()
    {
        var result = Keccak256.Hash(new byte[] { 0x41 });
        Assert.Equal(32, result.Length);
    }
}
