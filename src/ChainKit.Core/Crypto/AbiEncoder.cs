using System.Numerics;
using System.Text;
using ChainKit.Core.Extensions;

namespace ChainKit.Core.Crypto;

/// <summary>
/// Solidity ABI encoding/decoding utilities (chain-agnostic).
/// Address encoding is chain-specific — see TronAbiEncoder / EvmAbiEncoder.
/// </summary>
public static class AbiEncoder
{
    /// <summary>
    /// Computes the 4-byte function selector from a Solidity function signature.
    /// Example: "transfer(address,uint256)" → 0xa9059cbb
    /// </summary>
    public static byte[] EncodeFunctionSelector(string signature)
        => Keccak256.Hash(Encoding.UTF8.GetBytes(signature))[..4];

    /// <summary>
    /// ABI-encodes a uint256 value as a 32-byte big-endian array, left-padded with zeros.
    /// </summary>
    public static byte[] EncodeUint256(BigInteger value)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        var padded = new byte[32];
        Buffer.BlockCopy(bytes, 0, padded, 32 - bytes.Length, bytes.Length);
        return padded;
    }

    /// <summary>
    /// Decodes a 32-byte ABI-encoded uint256 value.
    /// </summary>
    public static BigInteger DecodeUint256(byte[] data)
    {
        var slice = data.Length > 32 ? data[^32..] : data;
        return new BigInteger(slice, isUnsigned: true, isBigEndian: true);
    }

    /// <summary>
    /// Decodes an ABI-encoded dynamic string (offset + length + UTF-8 data).
    /// Returns empty string if data is too short.
    /// </summary>
    public static string DecodeString(byte[] data)
    {
        if (data.Length < 64) return string.Empty;
        var length = (int)new BigInteger(data[32..64], isUnsigned: true, isBigEndian: true);
        return Encoding.UTF8.GetString(data, 64, length);
    }
}
