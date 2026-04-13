using System.Security.Cryptography;
using ChainKit.Core;
using NBitcoin;

namespace ChainKit.Evm.Crypto;

/// <summary>
/// EVM account with key management, supporting random generation, private key import, and BIP-44 mnemonic derivation.
/// Uses derivation path m/44'/60'/0'/0/{index} (Ethereum standard).
/// Implements IDisposable to zero private key memory on disposal.
/// </summary>
public sealed class EvmAccount : IAccount, IDisposable
{
    /// <summary>
    /// The EIP-55 checksummed address (0x-prefixed, 42 characters).
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// The compressed public key (33 bytes).
    /// </summary>
    public byte[] PublicKey { get; }

    /// <summary>
    /// The raw private key (32 bytes). Zeroed on Dispose().
    /// </summary>
    public byte[] PrivateKey { get; }

    private EvmAccount(byte[] privateKey, byte[] publicKey, string address)
    {
        PrivateKey = privateKey;
        PublicKey = publicKey;
        Address = address;
    }

    /// <summary>
    /// Zeros the private key memory.
    /// </summary>
    public void Dispose() => CryptographicOperations.ZeroMemory(PrivateKey);

    /// <summary>
    /// Creates a new account with a cryptographically random private key.
    /// </summary>
    public static EvmAccount Create() => FromPrivateKey(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Creates an account from an existing private key (32 bytes).
    /// </summary>
    public static EvmAccount FromPrivateKey(byte[] privateKey)
    {
        var ecKey = NBitcoin.Secp256k1.ECPrivKey.Create(privateKey);
        var pubKey = ecKey.CreatePubKey();
        var uncompressed = new byte[65];
        pubKey.WriteToSpan(false, uncompressed, out _);
        var address = EvmAddress.FromPublicKey(uncompressed);
        var compressed = new byte[33];
        pubKey.WriteToSpan(true, compressed, out _);
        return new EvmAccount(privateKey, compressed, address);
    }

    /// <summary>
    /// Creates an account from a BIP-39 mnemonic using derivation path m/44'/60'/0'/0/{index}.
    /// </summary>
    public static EvmAccount FromMnemonic(string mnemonic, int index = 0)
    {
        var m = new Mnemonic(mnemonic);
        var seed = m.DeriveSeed();
        var masterKey = ExtKey.CreateFromSeed(seed);
        var derived = masterKey.Derive(new KeyPath($"m/44'/60'/0'/0/{index}"));
        return FromPrivateKey(derived.PrivateKey.ToBytes());
    }
}
