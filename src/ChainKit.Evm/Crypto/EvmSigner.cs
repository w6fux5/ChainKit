using NBitcoin.Secp256k1;

namespace ChainKit.Evm.Crypto;

/// <summary>
/// ECDSA signing utilities for EVM transactions.
/// Supports EIP-1559 typed transactions (raw recovery id) and EIP-155 legacy transactions (chain-encoded v).
/// </summary>
public static class EvmSigner
{
    /// <summary>
    /// Signs a transaction hash for EIP-1559/EIP-2930 typed transactions.
    /// Returns 65 bytes: [r(32) | s(32) | v(1)] where v is the raw recovery id (0 or 1).
    /// </summary>
    public static byte[] SignTyped(byte[] txHash, byte[] privateKey)
    {
        var ecKey = ECPrivKey.Create(privateKey);
        if (!ecKey.TrySignRecoverable(txHash, out var sig) || sig is null)
            throw new InvalidOperationException("Failed to create recoverable signature.");
        var output = new byte[65];
        sig.WriteToSpanCompact(output.AsSpan(0, 64), out var recId);
        output[64] = (byte)recId;
        return output;
    }

    /// <summary>
    /// Signs a transaction hash for EIP-155 legacy transactions.
    /// Returns 65 bytes: [r(32) | s(32) | recId(1)] where recId is 0 or 1.
    /// The EIP-155 v-value (chainId * 2 + 35 + recId) is computed by the transaction builder,
    /// not here, to avoid byte overflow for chainId &gt; 110.
    /// </summary>
    public static byte[] SignLegacy(byte[] txHash, byte[] privateKey)
    {
        var ecKey = ECPrivKey.Create(privateKey);
        if (!ecKey.TrySignRecoverable(txHash, out var sig) || sig is null)
            throw new InvalidOperationException("Failed to create recoverable signature.");
        var output = new byte[65];
        sig.WriteToSpanCompact(output.AsSpan(0, 64), out var recId);
        output[64] = (byte)recId;
        return output;
    }

    /// <summary>
    /// Verifies a signature against data and a compressed public key.
    /// Supports both raw recovery id (0/1) and legacy recovery id (27/28).
    /// </summary>
    public static bool Verify(byte[] data, byte[] signature, byte[] publicKey)
    {
        if (signature.Length != 65) return false;
        var recId = signature[64] >= 27 ? signature[64] - 27 : signature[64];
        if (!SecpRecoverableECDSASignature.TryCreateFromCompact(signature.AsSpan(0, 64), recId, out var recSig))
            return false;
        if (!ECPubKey.TryRecover(Context.Instance, recSig, data, out var recoveredPubKey))
            return false;
        var recoveredBytes = new byte[33];
        recoveredPubKey.WriteToSpan(true, recoveredBytes, out _);
        return recoveredBytes.AsSpan().SequenceEqual(publicKey);
    }
}
