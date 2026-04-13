using System.Numerics;
using ChainKit.Core.Crypto;
using ChainKit.Core.Extensions;
using ChainKit.Evm.Crypto;

namespace ChainKit.Evm.Protocol;

/// <summary>
/// Utility methods for EVM transaction signing and hash computation.
/// </summary>
public static class EvmTransactionUtils
{
    /// <summary>
    /// Computes the Keccak-256 signing hash of an unsigned transaction.
    /// </summary>
    /// <param name="unsignedTx">The unsigned transaction bytes.</param>
    /// <returns>The 32-byte signing hash.</returns>
    public static byte[] ComputeSigningHash(byte[] unsignedTx) => Keccak256.Hash(unsignedTx);

    /// <summary>
    /// Computes the transaction hash of a signed transaction.
    /// </summary>
    /// <param name="signedTx">The signed transaction bytes.</param>
    /// <returns>The transaction hash as a 0x-prefixed hex string.</returns>
    public static string ComputeTxHash(byte[] signedTx) => "0x" + Keccak256.Hash(signedTx).ToHex();

    /// <summary>
    /// Signs an EIP-1559 transaction: build unsigned, hash, sign, build signed.
    /// Returns the transaction hash and the signed raw transaction bytes.
    /// </summary>
    /// <param name="chainId">The EIP-155 chain ID.</param>
    /// <param name="nonce">The sender's transaction count.</param>
    /// <param name="maxPriorityFeePerGas">The max priority fee (tip) in wei.</param>
    /// <param name="maxFeePerGas">The max total fee in wei.</param>
    /// <param name="gasLimit">The gas limit.</param>
    /// <param name="to">The recipient address (hex).</param>
    /// <param name="value">The value to transfer in wei.</param>
    /// <param name="data">The call data.</param>
    /// <param name="privateKey">The 32-byte private key.</param>
    /// <returns>A tuple of (txHash, rawTx) where txHash is the 0x-prefixed transaction hash.</returns>
    public static (string txHash, byte[] rawTx) SignEip1559Transaction(
        long chainId, long nonce,
        BigInteger maxPriorityFeePerGas, BigInteger maxFeePerGas, long gasLimit,
        string to, BigInteger value, byte[] data,
        byte[] privateKey)
    {
        var unsigned = EvmTransactionBuilder.BuildEip1559(chainId, nonce, maxPriorityFeePerGas, maxFeePerGas, gasLimit, to, value, data, null);
        var signingHash = ComputeSigningHash(unsigned);
        var signature = EvmSigner.SignTyped(signingHash, privateKey);
        var signed = EvmTransactionBuilder.BuildEip1559(chainId, nonce, maxPriorityFeePerGas, maxFeePerGas, gasLimit, to, value, data, signature);
        return (ComputeTxHash(signed), signed);
    }
}
