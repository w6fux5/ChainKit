using System.Numerics;
using System.Security.Cryptography;

namespace ChainKit.Core.Extensions;

public static class Base58Extensions
{
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    public static string ToBase58Check(this byte[] payload)
    {
        var checksum = ComputeChecksum(payload);
        var data = new byte[payload.Length + 4];
        Buffer.BlockCopy(payload, 0, data, 0, payload.Length);
        Buffer.BlockCopy(checksum, 0, data, payload.Length, 4);
        return EncodeBase58(data);
    }

    public static byte[] FromBase58Check(this string encoded)
    {
        var data = DecodeBase58(encoded);
        if (data.Length < 4)
            throw new FormatException("Base58Check data too short.");
        var payload = data[..^4];
        var checksum = data[^4..];
        var expectedChecksum = ComputeChecksum(payload);
        if (!checksum.AsSpan().SequenceEqual(expectedChecksum.AsSpan(0, 4)))
            throw new FormatException("Base58Check checksum mismatch.");
        return payload;
    }

    private static byte[] ComputeChecksum(byte[] data)
    {
        var hash1 = SHA256.HashData(data);
        var hash2 = SHA256.HashData(hash1);
        return hash2[..4];
    }

    private static string EncodeBase58(byte[] data)
    {
        var intData = new BigInteger(data, isUnsigned: true, isBigEndian: true);
        var result = new List<char>();
        while (intData > 0)
        {
            intData = BigInteger.DivRem(intData, 58, out var remainder);
            result.Add(Alphabet[(int)remainder]);
        }
        foreach (var b in data)
        {
            if (b != 0) break;
            result.Add(Alphabet[0]);
        }
        result.Reverse();
        return new string(result.ToArray());
    }

    private static byte[] DecodeBase58(string encoded)
    {
        var intData = BigInteger.Zero;
        foreach (var c in encoded)
        {
            var digit = Alphabet.IndexOf(c);
            if (digit < 0)
                throw new FormatException($"Invalid Base58 character: '{c}'");
            intData = intData * 58 + digit;
        }
        var leadingZeros = encoded.TakeWhile(c => c == '1').Count();
        var bytesWithoutLeading = intData.ToByteArray(isUnsigned: true, isBigEndian: true);
        var result = new byte[leadingZeros + bytesWithoutLeading.Length];
        Buffer.BlockCopy(bytesWithoutLeading, 0, result, leadingZeros, bytesWithoutLeading.Length);
        return result;
    }
}
