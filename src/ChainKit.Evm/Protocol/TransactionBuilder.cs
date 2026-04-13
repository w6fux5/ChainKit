using System.Numerics;
using ChainKit.Core.Extensions;

namespace ChainKit.Evm.Protocol;

/// <summary>
/// Builds EIP-1559 (Type 2) and Legacy (EIP-155) raw transactions using RLP encoding.
/// </summary>
public static class EvmTransactionBuilder
{
    /// <summary>
    /// Builds an EIP-1559 (Type 2) transaction.
    /// Unsigned (for signing hash): 0x02 || RLP([chainId, nonce, maxPriorityFee, maxFee, gasLimit, to, value, data, accessList])
    /// Signed: 0x02 || RLP([chainId, nonce, maxPriorityFee, maxFee, gasLimit, to, value, data, accessList, v, r, s])
    /// </summary>
    /// <param name="chainId">The EIP-155 chain ID.</param>
    /// <param name="nonce">The sender's transaction count.</param>
    /// <param name="maxPriorityFeePerGas">The max priority fee (tip) in wei.</param>
    /// <param name="maxFeePerGas">The max total fee in wei.</param>
    /// <param name="gasLimit">The gas limit.</param>
    /// <param name="to">The recipient address (hex, with or without 0x prefix).</param>
    /// <param name="value">The value to transfer in wei.</param>
    /// <param name="data">The call data.</param>
    /// <param name="signature">65-byte signature [r(32)|s(32)|v(1)] or null for unsigned.</param>
    /// <returns>The encoded transaction bytes (prefixed with 0x02 type byte).</returns>
    public static byte[] BuildEip1559(
        long chainId, long nonce,
        BigInteger maxPriorityFeePerGas, BigInteger maxFeePerGas, long gasLimit,
        string to, BigInteger value, byte[] data,
        byte[]? signature)
    {
        var toBytes = to.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? to[2..].FromHex() : to.FromHex();

        var items = new List<byte[]>
        {
            RlpEncoder.EncodeLong(chainId),
            RlpEncoder.EncodeLong(nonce),
            RlpEncoder.EncodeUint(maxPriorityFeePerGas),
            RlpEncoder.EncodeUint(maxFeePerGas),
            RlpEncoder.EncodeLong(gasLimit),
            RlpEncoder.EncodeElement(toBytes),
            RlpEncoder.EncodeUint(value),
            RlpEncoder.EncodeElement(data),
            RlpEncoder.EncodeList() // empty access list
        };

        if (signature != null && signature.Length == 65)
        {
            var v = signature[64];
            var r = new BigInteger(signature[..32], isUnsigned: true, isBigEndian: true);
            var s = new BigInteger(signature[32..64], isUnsigned: true, isBigEndian: true);
            items.Add(RlpEncoder.EncodeLong(v));
            items.Add(RlpEncoder.EncodeUint(r));
            items.Add(RlpEncoder.EncodeUint(s));
        }

        var rlpPayload = RlpEncoder.EncodeList(items.ToArray());
        // Prepend 0x02 type byte
        var result = new byte[1 + rlpPayload.Length];
        result[0] = 0x02;
        Buffer.BlockCopy(rlpPayload, 0, result, 1, rlpPayload.Length);
        return result;
    }

    /// <summary>
    /// Builds a Legacy (EIP-155) transaction.
    /// Signing hash: Keccak256(RLP([nonce, gasPrice, gasLimit, to, value, data, chainId, 0, 0]))
    /// Signed: RLP([nonce, gasPrice, gasLimit, to, value, data, v, r, s])
    /// </summary>
    /// <param name="nonce">The sender's transaction count.</param>
    /// <param name="gasPrice">The gas price in wei.</param>
    /// <param name="gasLimit">The gas limit.</param>
    /// <param name="to">The recipient address (hex, with or without 0x prefix).</param>
    /// <param name="value">The value to transfer in wei.</param>
    /// <param name="data">The call data.</param>
    /// <param name="chainId">The EIP-155 chain ID.</param>
    /// <param name="signature">65-byte signature [r(32)|s(32)|v(1)] or null for unsigned.</param>
    /// <returns>The RLP-encoded transaction bytes.</returns>
    public static byte[] BuildLegacy(
        long nonce, BigInteger gasPrice, long gasLimit,
        string to, BigInteger value, byte[] data,
        long chainId, byte[]? signature)
    {
        var toBytes = to.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? to[2..].FromHex() : to.FromHex();

        if (signature == null || signature.Length != 65)
        {
            // Unsigned for signing: include chainId, 0, 0 per EIP-155
            return RlpEncoder.EncodeList(
                RlpEncoder.EncodeLong(nonce),
                RlpEncoder.EncodeUint(gasPrice),
                RlpEncoder.EncodeLong(gasLimit),
                RlpEncoder.EncodeElement(toBytes),
                RlpEncoder.EncodeUint(value),
                RlpEncoder.EncodeElement(data),
                RlpEncoder.EncodeLong(chainId),
                RlpEncoder.EncodeElement(Array.Empty<byte>()),
                RlpEncoder.EncodeElement(Array.Empty<byte>())
            );
        }

        var v = (long)signature[64];
        var r = new BigInteger(signature[..32], isUnsigned: true, isBigEndian: true);
        var s = new BigInteger(signature[32..64], isUnsigned: true, isBigEndian: true);

        return RlpEncoder.EncodeList(
            RlpEncoder.EncodeLong(nonce),
            RlpEncoder.EncodeUint(gasPrice),
            RlpEncoder.EncodeLong(gasLimit),
            RlpEncoder.EncodeElement(toBytes),
            RlpEncoder.EncodeUint(value),
            RlpEncoder.EncodeElement(data),
            RlpEncoder.EncodeLong(v),
            RlpEncoder.EncodeUint(r),
            RlpEncoder.EncodeUint(s)
        );
    }
}
