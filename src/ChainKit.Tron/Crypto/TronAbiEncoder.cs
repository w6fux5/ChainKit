using System.Numerics;
using ChainKit.Core.Crypto;
using ChainKit.Core.Extensions;

namespace ChainKit.Tron.Crypto;

/// <summary>
/// Tron-specific ABI encoding/decoding for addresses with the 41 hex prefix.
/// Generic ABI methods delegate to <see cref="ChainKit.Core.Crypto.AbiEncoder"/>.
/// </summary>
public static class TronAbiEncoder
{
    /// <summary>
    /// ABI-encodes a Tron hex address (with or without 41 prefix) as a 32-byte padded value.
    /// </summary>
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

    /// <summary>
    /// Decodes a 32-byte ABI-encoded value back to a Tron hex address (with 41 prefix).
    /// </summary>
    public static string DecodeAddress(byte[] data)
    {
        var slice = data.Length > 32 ? data[^32..] : data;
        return "41" + slice[12..].ToHex();
    }

    /// <summary>
    /// Encodes a TRC20 transfer(address,uint256) call.
    /// </summary>
    public static byte[] EncodeTransfer(string toHex, BigInteger amount)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("transfer(address,uint256)");
        var addr = EncodeAddress(toHex);
        var amt = AbiEncoder.EncodeUint256(amount);
        return ConcatBytes(selector, addr, amt);
    }

    /// <summary>
    /// Encodes a TRC20 balanceOf(address) call.
    /// </summary>
    public static byte[] EncodeBalanceOf(string addressHex)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("balanceOf(address)");
        var addr = EncodeAddress(addressHex);
        return ConcatBytes(selector, addr);
    }

    /// <summary>
    /// Encodes a TRC20 approve(address,uint256) call.
    /// </summary>
    public static byte[] EncodeApprove(string spenderHex, BigInteger amount)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("approve(address,uint256)");
        var spender = EncodeAddress(spenderHex);
        var amt = AbiEncoder.EncodeUint256(amount);
        return ConcatBytes(selector, spender, amt);
    }

    /// <summary>
    /// Encodes a TRC20 mint(address,uint256) call.
    /// </summary>
    public static byte[] EncodeMint(string toHex, BigInteger amount)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("mint(address,uint256)");
        var addr = EncodeAddress(toHex);
        var amt = AbiEncoder.EncodeUint256(amount);
        return ConcatBytes(selector, addr, amt);
    }

    /// <summary>
    /// Encodes a TRC20 burn(uint256) call.
    /// </summary>
    public static byte[] EncodeBurn(BigInteger amount)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("burn(uint256)");
        var amt = AbiEncoder.EncodeUint256(amount);
        return ConcatBytes(selector, amt);
    }

    /// <summary>
    /// Encodes a TRC20 burnFrom(address,uint256) call.
    /// </summary>
    public static byte[] EncodeBurnFrom(string fromHex, BigInteger amount)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("burnFrom(address,uint256)");
        var addr = EncodeAddress(fromHex);
        var amt = AbiEncoder.EncodeUint256(amount);
        return ConcatBytes(selector, addr, amt);
    }

    /// <summary>
    /// Encodes a TRC20 allowance(address,address) call.
    /// </summary>
    public static byte[] EncodeAllowance(string ownerHex, string spenderHex)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("allowance(address,address)");
        var owner = EncodeAddress(ownerHex);
        var spender = EncodeAddress(spenderHex);
        return ConcatBytes(selector, owner, spender);
    }

    private static byte[] ConcatBytes(params byte[][] arrays)
    {
        int totalLength = 0;
        foreach (var arr in arrays)
            totalLength += arr.Length;

        var result = new byte[totalLength];
        int offset = 0;
        foreach (var arr in arrays)
        {
            Buffer.BlockCopy(arr, 0, result, offset, arr.Length);
            offset += arr.Length;
        }
        return result;
    }
}
