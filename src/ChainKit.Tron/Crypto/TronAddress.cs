using ChainKit.Core.Extensions;

namespace ChainKit.Tron.Crypto;

public static class TronAddress
{
    private const byte AddressPrefix = 0x41;

    public static bool IsValid(string address)
    {
        if (string.IsNullOrEmpty(address)) return false;

        if (address.StartsWith('T') && address.Length >= 25 && address.Length <= 36)
        {
            try { var d = address.FromBase58Check(); return d.Length == 21 && d[0] == AddressPrefix; }
            catch (FormatException) { return false; }
        }

        if (address.Length == 42 && address.StartsWith("41", StringComparison.OrdinalIgnoreCase))
        {
            try { address.FromHex(); return true; }
            catch { return false; }
        }

        return false;
    }

    public static string ToBase58(string hexAddress) => hexAddress.FromHex().ToBase58Check();
    public static string ToHex(string base58Address) => base58Address.FromBase58Check().ToHex();
}
