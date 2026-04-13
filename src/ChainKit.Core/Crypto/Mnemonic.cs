namespace ChainKit.Core.Crypto;

public static class Mnemonic
{
    public static string Generate(int wordCount = 12)
    {
        var entropy = wordCount switch
        {
            12 => NBitcoin.WordCount.Twelve,
            24 => NBitcoin.WordCount.TwentyFour,
            _ => throw new ArgumentException("wordCount must be 12 or 24")
        };
        return new NBitcoin.Mnemonic(NBitcoin.Wordlist.English, entropy).ToString();
    }

    public static byte[] ToSeed(string mnemonic, string passphrase = "")
        => new NBitcoin.Mnemonic(mnemonic).DeriveSeed(passphrase);

    public static bool Validate(string mnemonic)
    {
        try { return new NBitcoin.Mnemonic(mnemonic).IsValidChecksum; }
        catch { return false; }
    }
}
