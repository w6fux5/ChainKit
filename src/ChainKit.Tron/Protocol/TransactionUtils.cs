using System.Security.Cryptography;
using ChainKit.Tron.Crypto;
using ChainKit.Tron.Protocol.Protobuf;
using Google.Protobuf;

namespace ChainKit.Tron.Protocol;

/// <summary>
/// Utility methods for Tron transaction hashing and signing.
/// </summary>
public static class TransactionUtils
{
    /// <summary>
    /// Computes the transaction ID (SHA-256 hash of the serialized raw data).
    /// </summary>
    /// <param name="transaction">The transaction whose ID to compute.</param>
    /// <returns>32-byte transaction hash.</returns>
    public static byte[] ComputeTxId(Transaction transaction)
    {
        if (transaction.RawData is null)
            throw new ArgumentException("Transaction has no raw data.", nameof(transaction));

        var rawBytes = transaction.RawData.ToByteArray();
        return SHA256.HashData(rawBytes);
    }

    /// <summary>
    /// Signs a transaction with the given private key and returns a new transaction
    /// with the signature attached.
    /// </summary>
    /// <param name="transaction">The unsigned transaction.</param>
    /// <param name="privateKey">32-byte secp256k1 private key.</param>
    /// <returns>A new transaction with the signature added.</returns>
    public static Transaction Sign(Transaction transaction, byte[] privateKey)
    {
        var txId = ComputeTxId(transaction);
        var signature = TronSigner.Sign(txId, privateKey);

        var signed = transaction.Clone();
        signed.Signature.Add(ByteString.CopyFrom(signature));
        return signed;
    }

    /// <summary>
    /// Adds a pre-computed signature to a transaction and returns a new transaction.
    /// </summary>
    /// <param name="transaction">The transaction to add the signature to.</param>
    /// <param name="signature">65-byte recoverable ECDSA signature (r + s + v).</param>
    /// <returns>A new transaction with the signature added.</returns>
    public static Transaction AddSignature(Transaction transaction, byte[] signature)
    {
        if (signature.Length != 65)
            throw new ArgumentException("Signature must be 65 bytes (r[32] + s[32] + v[1]).", nameof(signature));

        var result = transaction.Clone();
        result.Signature.Add(ByteString.CopyFrom(signature));
        return result;
    }
}
