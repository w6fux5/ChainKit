using System.Numerics;
using System.Text.Json;

namespace ChainKit.Evm.Providers;

/// <summary>
/// Provider interface for EVM-compatible blockchain JSON-RPC operations.
/// </summary>
public interface IEvmProvider : IDisposable
{
    /// <summary>
    /// Gets the balance of an address in wei.
    /// </summary>
    Task<BigInteger> GetBalanceAsync(string address, CancellationToken ct = default);

    /// <summary>
    /// Gets the transaction count (nonce) of an address.
    /// </summary>
    Task<long> GetTransactionCountAsync(string address, CancellationToken ct = default);

    /// <summary>
    /// Gets the contract bytecode at an address. Returns "0x" for non-contract addresses.
    /// </summary>
    Task<string> GetCodeAsync(string address, CancellationToken ct = default);

    /// <summary>
    /// Gets a block by its number. Returns null if the block does not exist.
    /// </summary>
    Task<JsonElement?> GetBlockByNumberAsync(long blockNumber, bool fullTx = false, CancellationToken ct = default);

    /// <summary>
    /// Gets the latest block number.
    /// </summary>
    Task<long> GetBlockNumberAsync(CancellationToken ct = default);

    /// <summary>
    /// Broadcasts a signed transaction. Returns the transaction hash.
    /// </summary>
    Task<string> SendRawTransactionAsync(byte[] signedTx, CancellationToken ct = default);

    /// <summary>
    /// Gets a transaction by its hash. Returns null if not found.
    /// </summary>
    Task<JsonElement?> GetTransactionByHashAsync(string txHash, CancellationToken ct = default);

    /// <summary>
    /// Gets a transaction receipt by its hash. Returns null if not yet mined.
    /// </summary>
    Task<JsonElement?> GetTransactionReceiptAsync(string txHash, CancellationToken ct = default);

    /// <summary>
    /// Executes a read-only contract call (eth_call). Returns the hex-encoded return data.
    /// </summary>
    Task<string> CallAsync(string to, byte[] data, CancellationToken ct = default);

    /// <summary>
    /// Estimates the gas required for a transaction.
    /// </summary>
    Task<long> EstimateGasAsync(string from, string to, byte[] data, BigInteger? value = null, CancellationToken ct = default);

    /// <summary>
    /// Gets the current gas price in wei (legacy pricing).
    /// </summary>
    Task<BigInteger> GetGasPriceAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the current EIP-1559 fee parameters: base fee from the latest block and suggested priority fee.
    /// </summary>
    Task<(BigInteger baseFee, BigInteger priorityFee)> GetEip1559FeesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets event logs matching the given filter criteria.
    /// </summary>
    Task<JsonElement[]> GetLogsAsync(long fromBlock, long toBlock, string? address = null, string[]? topics = null, CancellationToken ct = default);

    /// <summary>
    /// Gets the chain ID reported by the node via eth_chainId.
    /// </summary>
    Task<long> GetChainIdAsync(CancellationToken ct = default);
}
