namespace ChainKit.Core.Extensions;

public static class HexExtensions
{
    public static string ToHex(this byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static byte[] FromHex(this string hex)
    {
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];
        return Convert.FromHexString(hex);
    }
}
