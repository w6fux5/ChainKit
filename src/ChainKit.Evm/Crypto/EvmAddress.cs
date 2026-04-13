using System.Text;
using ChainKit.Core.Crypto;
using ChainKit.Core.Extensions;

namespace ChainKit.Evm.Crypto;

/// <summary>
/// EVM address utilities — validation, EIP-55 checksum encoding, and public key derivation.
/// </summary>
public static class EvmAddress
{
    /// <summary>
    /// Validates whether a string is a well-formed EVM address (0x-prefixed, 40 hex characters).
    /// </summary>
    public static bool IsValid(string address)
    {
        if (string.IsNullOrEmpty(address)) return false;
        if (!address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return false;
        if (address.Length != 42) return false;
        return address[2..].All(c => Uri.IsHexDigit(c));
    }

    /// <summary>
    /// Converts an EVM address to its EIP-55 mixed-case checksum representation.
    /// </summary>
    public static string ToChecksumAddress(string address)
    {
        var addr = address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? address[2..].ToLowerInvariant()
            : address.ToLowerInvariant();
        var hash = Keccak256.Hash(Encoding.UTF8.GetBytes(addr)).ToHex();
        var result = new StringBuilder("0x", 42);
        for (int i = 0; i < 40; i++)
            result.Append(hash[i] >= '8' ? char.ToUpperInvariant(addr[i]) : addr[i]);
        return result.ToString();
    }

    /// <summary>
    /// Derives an EVM address from a 65-byte uncompressed public key (04 prefix + 64 bytes).
    /// </summary>
    public static string FromPublicKey(byte[] uncompressedPublicKey)
    {
        var hash = Keccak256.Hash(uncompressedPublicKey[1..]);
        var addressBytes = hash[12..];
        return ToChecksumAddress("0x" + addressBytes.ToHex());
    }
}
