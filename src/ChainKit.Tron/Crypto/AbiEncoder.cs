using System.Numerics;
using System.Text;
using ChainKit.Core.Extensions;

namespace ChainKit.Tron.Crypto;

public static class AbiEncoder
{
    public static byte[] EncodeFunctionSelector(string signature)
        => Keccak256.Hash(Encoding.UTF8.GetBytes(signature))[..4];

    public static byte[] EncodeAddress(string hexAddress)
    {
        var addr = hexAddress;
        if (addr.StartsWith("41") && addr.Length == 42)
            addr = addr[2..];
        var bytes = addr.FromHex();
        var padded = new byte[32];
        Buffer.BlockCopy(bytes, 0, padded, 32 - bytes.Length, bytes.Length);
        return padded;
    }

    public static byte[] EncodeUint256(BigInteger value)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        var padded = new byte[32];
        Buffer.BlockCopy(bytes, 0, padded, 32 - bytes.Length, bytes.Length);
        return padded;
    }

    public static byte[] EncodeTransfer(string toHex, BigInteger amount)
    {
        var selector = EncodeFunctionSelector("transfer(address,uint256)");
        var addr = EncodeAddress(toHex);
        var amt = EncodeUint256(amount);
        var result = new byte[selector.Length + addr.Length + amt.Length];
        Buffer.BlockCopy(selector, 0, result, 0, selector.Length);
        Buffer.BlockCopy(addr, 0, result, selector.Length, addr.Length);
        Buffer.BlockCopy(amt, 0, result, selector.Length + addr.Length, amt.Length);
        return result;
    }

    public static byte[] EncodeBalanceOf(string addressHex)
    {
        var selector = EncodeFunctionSelector("balanceOf(address)");
        var addr = EncodeAddress(addressHex);
        var result = new byte[selector.Length + addr.Length];
        Buffer.BlockCopy(selector, 0, result, 0, selector.Length);
        Buffer.BlockCopy(addr, 0, result, selector.Length, addr.Length);
        return result;
    }

    public static byte[] EncodeApprove(string spenderHex, BigInteger amount)
    {
        var selector = EncodeFunctionSelector("approve(address,uint256)");
        var spender = EncodeAddress(spenderHex);
        var amt = EncodeUint256(amount);
        var result = new byte[selector.Length + spender.Length + amt.Length];
        Buffer.BlockCopy(selector, 0, result, 0, selector.Length);
        Buffer.BlockCopy(spender, 0, result, selector.Length, spender.Length);
        Buffer.BlockCopy(amt, 0, result, selector.Length + spender.Length, amt.Length);
        return result;
    }

    public static byte[] EncodeMint(string toHex, BigInteger amount)
    {
        var selector = EncodeFunctionSelector("mint(address,uint256)");
        var addr = EncodeAddress(toHex);
        var amt = EncodeUint256(amount);
        var result = new byte[selector.Length + addr.Length + amt.Length];
        Buffer.BlockCopy(selector, 0, result, 0, selector.Length);
        Buffer.BlockCopy(addr, 0, result, selector.Length, addr.Length);
        Buffer.BlockCopy(amt, 0, result, selector.Length + addr.Length, amt.Length);
        return result;
    }

    public static byte[] EncodeBurn(BigInteger amount)
    {
        var selector = EncodeFunctionSelector("burn(uint256)");
        var amt = EncodeUint256(amount);
        var result = new byte[selector.Length + amt.Length];
        Buffer.BlockCopy(selector, 0, result, 0, selector.Length);
        Buffer.BlockCopy(amt, 0, result, selector.Length, amt.Length);
        return result;
    }

    public static BigInteger DecodeUint256(byte[] data)
    {
        var slice = data.Length > 32 ? data[^32..] : data;
        return new BigInteger(slice, isUnsigned: true, isBigEndian: true);
    }

    public static string DecodeAddress(byte[] data)
    {
        var slice = data.Length > 32 ? data[^32..] : data;
        return "41" + slice[12..].ToHex();
    }

    public static string DecodeString(byte[] data)
    {
        if (data.Length < 64) return string.Empty;
        var length = (int)new BigInteger(data[32..64], isUnsigned: true, isBigEndian: true);
        return Encoding.UTF8.GetString(data, 64, length);
    }
}
