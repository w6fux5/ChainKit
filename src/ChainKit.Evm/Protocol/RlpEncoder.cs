using System.Numerics;

namespace ChainKit.Evm.Protocol;

/// <summary>
/// Recursive Length Prefix (RLP) encoding for Ethereum transaction serialization.
/// Spec: https://ethereum.org/en/developers/docs/data-structures-and-encoding/rlp/
/// </summary>
public static class RlpEncoder
{
    /// <summary>
    /// RLP-encodes a single byte array element (string item).
    /// </summary>
    /// <param name="data">The raw bytes to encode.</param>
    /// <returns>The RLP-encoded byte array.</returns>
    public static byte[] EncodeElement(byte[] data)
    {
        if (data.Length == 1 && data[0] < 0x80)
            return data;

        return EncodeWithPrefix(data, 0x80);
    }

    /// <summary>
    /// RLP-encodes a list of already-encoded items.
    /// </summary>
    /// <param name="items">Pre-encoded RLP items to concatenate into a list.</param>
    /// <returns>The RLP-encoded list.</returns>
    public static byte[] EncodeList(params byte[][] items)
    {
        var totalLength = items.Sum(item => item.Length);
        var payload = new byte[totalLength];
        var offset = 0;
        foreach (var item in items)
        {
            Buffer.BlockCopy(item, 0, payload, offset, item.Length);
            offset += item.Length;
        }

        return EncodeWithPrefix(payload, 0xc0);
    }

    /// <summary>
    /// RLP-encodes a <see cref="BigInteger"/> as an unsigned big-endian byte sequence.
    /// Zero encodes as empty bytes (0x80).
    /// </summary>
    /// <param name="value">The unsigned integer value to encode.</param>
    /// <returns>The RLP-encoded byte array.</returns>
    public static byte[] EncodeUint(BigInteger value)
    {
        if (value.IsZero)
            return EncodeElement(Array.Empty<byte>());

        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        return EncodeElement(bytes);
    }

    /// <summary>
    /// RLP-encodes a <see cref="long"/> value.
    /// Zero encodes as empty bytes (0x80).
    /// </summary>
    /// <param name="value">The integer value to encode.</param>
    /// <returns>The RLP-encoded byte array.</returns>
    public static byte[] EncodeLong(long value)
    {
        if (value == 0)
            return EncodeElement(Array.Empty<byte>());

        return EncodeUint(new BigInteger(value));
    }

    private static byte[] EncodeWithPrefix(byte[] data, byte shortBase)
    {
        if (data.Length <= 55)
        {
            var result = new byte[1 + data.Length];
            result[0] = (byte)(shortBase + data.Length);
            Buffer.BlockCopy(data, 0, result, 1, data.Length);
            return result;
        }

        var lengthBytes = EncodeLengthBytes(data.Length);
        var longBase = (byte)(shortBase + 55 + lengthBytes.Length);
        var output = new byte[1 + lengthBytes.Length + data.Length];
        output[0] = longBase;
        Buffer.BlockCopy(lengthBytes, 0, output, 1, lengthBytes.Length);
        Buffer.BlockCopy(data, 0, output, 1 + lengthBytes.Length, data.Length);
        return output;
    }

    private static byte[] EncodeLengthBytes(int length)
    {
        if (length < 256)
            return new byte[] { (byte)length };
        if (length < 65536)
            return new byte[] { (byte)(length >> 8), (byte)length };
        if (length < 16777216)
            return new byte[] { (byte)(length >> 16), (byte)(length >> 8), (byte)length };
        return new byte[] { (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length };
    }
}
