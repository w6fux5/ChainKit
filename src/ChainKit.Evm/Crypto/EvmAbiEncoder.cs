using System.Numerics;
using ChainKit.Core.Crypto;
using ChainKit.Core.Extensions;

namespace ChainKit.Evm.Crypto;

/// <summary>
/// EVM-specific ABI encoding utilities for ERC-20 contract interactions.
/// Handles 0x-prefixed address encoding/decoding and common function call encoding.
/// </summary>
public static class EvmAbiEncoder
{
    /// <summary>
    /// ABI-encodes an EVM address as a 32-byte left-padded value.
    /// </summary>
    public static byte[] EncodeAddress(string address)
    {
        var hex = address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? address[2..] : address;
        var bytes = hex.FromHex();
        var padded = new byte[32];
        Buffer.BlockCopy(bytes, 0, padded, 32 - bytes.Length, bytes.Length);
        return padded;
    }

    /// <summary>
    /// Decodes a 32-byte ABI-encoded address to an EIP-55 checksummed 0x address.
    /// </summary>
    public static string DecodeAddress(byte[] data)
    {
        var slice = data.Length > 32 ? data[^32..] : data;
        return EvmAddress.ToChecksumAddress("0x" + slice[12..].ToHex());
    }

    /// <summary>
    /// Encodes an ERC-20 transfer(address,uint256) call.
    /// </summary>
    public static byte[] EncodeTransfer(string toAddress, BigInteger amount)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("transfer(address,uint256)");
        var addr = EncodeAddress(toAddress);
        var amt = AbiEncoder.EncodeUint256(amount);
        return ConcatBytes(selector, addr, amt);
    }

    /// <summary>
    /// Encodes an ERC-20 balanceOf(address) call.
    /// </summary>
    public static byte[] EncodeBalanceOf(string address)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("balanceOf(address)");
        var addr = EncodeAddress(address);
        return ConcatBytes(selector, addr);
    }

    /// <summary>
    /// Encodes an ERC-20 approve(address,uint256) call.
    /// </summary>
    public static byte[] EncodeApprove(string spender, BigInteger amount)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("approve(address,uint256)");
        var spAddr = EncodeAddress(spender);
        var amt = AbiEncoder.EncodeUint256(amount);
        return ConcatBytes(selector, spAddr, amt);
    }

    /// <summary>
    /// Encodes an ERC-20 allowance(address,address) call.
    /// </summary>
    public static byte[] EncodeAllowance(string owner, string spender)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("allowance(address,address)");
        var ownerAddr = EncodeAddress(owner);
        var spenderAddr = EncodeAddress(spender);
        return ConcatBytes(selector, ownerAddr, spenderAddr);
    }

    private static byte[] ConcatBytes(params byte[][] arrays)
    {
        var totalLength = arrays.Sum(a => a.Length);
        var result = new byte[totalLength];
        var offset = 0;
        foreach (var arr in arrays)
        {
            Buffer.BlockCopy(arr, 0, result, offset, arr.Length);
            offset += arr.Length;
        }
        return result;
    }
}
