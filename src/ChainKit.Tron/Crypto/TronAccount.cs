using System.Security.Cryptography;
using ChainKit.Core;
using ChainKit.Core.Crypto;
using ChainKit.Core.Extensions;
using NBitcoin;

namespace ChainKit.Tron.Crypto;

public class TronAccount : IAccount, IDisposable
{
    public string Address { get; }
    public string HexAddress { get; }
    public byte[] PublicKey { get; }
    public byte[] PrivateKey { get; }

    private TronAccount(byte[] privateKey, byte[] publicKey, string hexAddress, string address)
    {
        PrivateKey = privateKey;
        PublicKey = publicKey;
        HexAddress = hexAddress;
        Address = address;
    }

    /// <summary>
    /// Zeroes private key material from memory.
    /// </summary>
    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(PrivateKey);
    }

    public static TronAccount Create()
        => FromPrivateKey(RandomNumberGenerator.GetBytes(32));

    public static TronAccount FromPrivateKey(byte[] privateKey)
    {
        var ecKey = NBitcoin.Secp256k1.ECPrivKey.Create(privateKey);
        var pubKey = ecKey.CreatePubKey();

        // Uncompressed public key (65 bytes: 04 + x + y)
        var pubBytes = new byte[65];
        pubKey.WriteToSpan(false, pubBytes, out _);

        // Tron address: Keccak256(pubkey[1..]) → last 20 bytes → prefix 0x41
        var hash = Keccak256.Hash(pubBytes[1..]);
        var addressBytes = new byte[21];
        addressBytes[0] = 0x41;
        Buffer.BlockCopy(hash, 12, addressBytes, 1, 20);

        var hexAddress = addressBytes.ToHex();
        var base58Address = TronAddress.ToBase58(hexAddress);

        // Store compressed public key (33 bytes)
        var compressedPub = new byte[33];
        pubKey.WriteToSpan(true, compressedPub, out _);

        return new TronAccount(privateKey, compressedPub, hexAddress, base58Address);
    }

    public static TronAccount FromMnemonic(string mnemonic, int index = 0)
    {
        var m = new NBitcoin.Mnemonic(mnemonic);
        var seed = m.DeriveSeed();
        var masterKey = ExtKey.CreateFromSeed(seed);
        var derived = masterKey.Derive(new KeyPath($"m/44'/195'/0'/0/{index}"));
        return FromPrivateKey(derived.PrivateKey.ToBytes());
    }
}
