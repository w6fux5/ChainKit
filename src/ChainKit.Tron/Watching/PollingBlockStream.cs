using ChainKit.Tron.Models;
using ChainKit.Tron.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChainKit.Tron.Watching;

public class PollingBlockStream : ITronBlockStream
{
    private readonly ITronProvider _provider;
    private readonly int _intervalMs;
    private readonly ILogger _logger;

    public PollingBlockStream(ITronProvider provider, int intervalMs = 3000,
        ILogger<PollingBlockStream>? logger = null)
    {
        _provider = provider;
        _intervalMs = intervalMs;
        _logger = logger ?? NullLogger<PollingBlockStream>.Instance;
    }

    public async IAsyncEnumerable<TronBlock> StreamBlocksAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        long lastBlockNumber = -1;

        while (!ct.IsCancellationRequested)
        {
            TronBlock? block = null;
            try
            {
                var blockInfo = await _provider.GetNowBlockAsync(ct);
                if (blockInfo.BlockNumber > lastBlockNumber)
                {
                    // If GetNowBlockAsync didn't include transactions,
                    // fetch the full block by number to get them.
                    if (blockInfo.Transactions == null || blockInfo.Transactions.Count == 0)
                    {
                        if (blockInfo.TransactionCount > 0)
                        {
                            var fullBlock = await _provider.GetBlockByNumAsync(blockInfo.BlockNumber, ct);
                            blockInfo = fullBlock;
                        }
                    }

                    lastBlockNumber = blockInfo.BlockNumber;
                    var transactions = ConvertTransactions(blockInfo.Transactions);
                    block = new TronBlock(
                        blockInfo.BlockNumber,
                        blockInfo.BlockId,
                        DateTimeOffset.FromUnixTimeMilliseconds(blockInfo.Timestamp),
                        transactions);
                }
            }
            catch (OperationCanceledException) { yield break; }
            catch (Exception ex) { _logger.LogDebug(ex, "Block polling failed, retrying next interval"); }

            if (block != null)
                yield return block;

            try { await Task.Delay(_intervalMs, ct); }
            catch (OperationCanceledException) { yield break; }
        }
    }

    internal static IReadOnlyList<TronBlockTransaction> ConvertTransactions(
        IReadOnlyList<BlockTransactionInfo>? txInfos)
    {
        if (txInfos == null || txInfos.Count == 0)
            return Array.Empty<TronBlockTransaction>();

        var result = new List<TronBlockTransaction>(txInfos.Count);
        foreach (var tx in txInfos)
        {
            result.Add(new TronBlockTransaction(
                tx.TxId,
                tx.OwnerAddress,
                tx.ToAddress,
                tx.ContractType,
                BuildRawData(tx)));
        }
        return result;
    }

    /// <summary>
    /// Packs the parsed transaction fields into a compact byte array that
    /// TronTransactionWatcher can later decode without re-fetching from the network.
    /// Layout: [8 bytes amount (big-endian)] [contract-address-bytes or empty] [data-bytes or empty]
    /// with a 4-byte length prefix for contract address and data sections.
    /// </summary>
    internal static byte[] BuildRawData(BlockTransactionInfo tx)
    {
        var contractAddr = tx.ContractAddress != null
            ? System.Text.Encoding.UTF8.GetBytes(tx.ContractAddress)
            : Array.Empty<byte>();
        var data = tx.Data ?? Array.Empty<byte>();

        // Layout: [8: amount][4: contractAddr len][contractAddr bytes][4: data len][data bytes]
        var buffer = new byte[8 + 4 + contractAddr.Length + 4 + data.Length];
        WriteBigEndianInt64(buffer, 0, tx.Amount);
        WriteBigEndianInt32(buffer, 8, contractAddr.Length);
        if (contractAddr.Length > 0)
            Buffer.BlockCopy(contractAddr, 0, buffer, 12, contractAddr.Length);
        WriteBigEndianInt32(buffer, 12 + contractAddr.Length, data.Length);
        if (data.Length > 0)
            Buffer.BlockCopy(data, 0, buffer, 16 + contractAddr.Length, data.Length);
        return buffer;
    }

    private static void WriteBigEndianInt64(byte[] buf, int offset, long value)
    {
        buf[offset]     = (byte)(value >> 56);
        buf[offset + 1] = (byte)(value >> 48);
        buf[offset + 2] = (byte)(value >> 40);
        buf[offset + 3] = (byte)(value >> 32);
        buf[offset + 4] = (byte)(value >> 24);
        buf[offset + 5] = (byte)(value >> 16);
        buf[offset + 6] = (byte)(value >> 8);
        buf[offset + 7] = (byte)value;
    }

    private static void WriteBigEndianInt32(byte[] buf, int offset, int value)
    {
        buf[offset]     = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }
}
