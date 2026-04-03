using NBitcoin.Secp256k1;

namespace ChainKit.Tron.Crypto;

public static class TronSigner
{
    public static byte[] Sign(byte[] data, byte[] privateKey)
    {
        var ecKey = ECPrivKey.Create(privateKey);
        if (!ecKey.TrySignRecoverable(data, out var sig) || sig is null)
            throw new InvalidOperationException("Failed to create recoverable signature.");
        var output = new byte[65];
        sig.WriteToSpanCompact(output.AsSpan(0, 64), out var recId);
        output[64] = (byte)recId;
        return output;
    }

    public static bool Verify(byte[] data, byte[] signature, byte[] publicKey)
    {
        if (signature.Length != 65) return false;
        var recId = signature[64];
        if (!SecpRecoverableECDSASignature.TryCreateFromCompact(signature.AsSpan(0, 64), recId, out var recSig))
            return false;
        if (!ECPubKey.TryRecover(Context.Instance, recSig, data, out var recoveredPubKey))
            return false;
        var recoveredBytes = new byte[33];
        recoveredPubKey.WriteToSpan(true, recoveredBytes, out _);
        return recoveredBytes.AsSpan().SequenceEqual(publicKey);
    }
}
