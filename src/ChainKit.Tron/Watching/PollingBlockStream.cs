using ChainKit.Tron.Models;
using ChainKit.Tron.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChainKit.Tron.Watching;

public sealed class PollingBlockStream : ITronBlockStream
{
    private readonly ITronProvider _provider;
    private readonly int _intervalMs;
    private readonly int _maxBlocksPerPoll;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new PollingBlockStream instance.
    /// </summary>
    /// <param name="provider">The Tron provider used for block fetches.</param>
    /// <param name="intervalMs">Interval between polls in milliseconds. Defaults to 3000 (matches Tron block time).</param>
    /// <param name="maxBlocksPerPoll">Upper bound on blocks yielded per poll cycle. When the chain has advanced by more
    /// than this, the remaining blocks are caught up on subsequent polls. Protects against unbounded serial fetches
    /// (and unresponsive cancellation) after long disconnects / restarts when head may have jumped thousands of blocks.</param>
    /// <param name="logger">Optional logger.</param>
    public PollingBlockStream(ITronProvider provider, int intervalMs = 3000,
        int maxBlocksPerPoll = 100,
        ILogger<PollingBlockStream>? logger = null)
    {
        if (maxBlocksPerPoll <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBlocksPerPoll), "Must be positive.");
        _provider = provider;
        _intervalMs = intervalMs;
        _maxBlocksPerPoll = maxBlocksPerPoll;
        _logger = logger ?? NullLogger<PollingBlockStream>.Instance;
    }

    public async IAsyncEnumerable<TronBlock> StreamBlocksAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        long lastBlockNumber = -1;

        while (!ct.IsCancellationRequested)
        {
            // Phase 1: fetch current head to learn the target we need to catch up to.
            BlockInfo? headBlock = null;
            try
            {
                headBlock = await _provider.GetNowBlockAsync(ct);
            }
            catch (OperationCanceledException) { yield break; }
            catch (Exception ex) { _logger.LogDebug(ex, "Block polling failed, retrying next interval"); }

            if (headBlock != null)
            {
                var currentHead = headBlock.BlockNumber;

                // On first successful poll, seed at (head - 1) so we don't backfill
                // the entire chain — we only want blocks produced from now forward.
                if (lastBlockNumber < 0)
                    lastBlockNumber = currentHead - 1;

                // Phase 2: yield every block in (lastBlockNumber, batchEnd], where batchEnd
                // caps the number of blocks yielded per poll. Tron block time ~= poll interval,
                // so head can advance by more than 1 between polls; without this loop,
                // intermediate blocks are silently dropped. The cap protects against
                // unbounded serial fetches after long disconnects / restarts.
                var gap = currentHead - lastBlockNumber;
                var batchEnd = gap > _maxBlocksPerPoll
                    ? lastBlockNumber + _maxBlocksPerPoll
                    : currentHead;
                if (gap > _maxBlocksPerPoll)
                {
                    _logger.LogWarning(
                        "Block stream is {Gap} blocks behind head {Head}; catching up {Cap} per poll",
                        gap, currentHead, _maxBlocksPerPoll);
                }

                while (lastBlockNumber < batchEnd && !ct.IsCancellationRequested)
                {
                    var num = lastBlockNumber + 1;
                    TronBlock? block = null;
                    try
                    {
                        var bi = num == currentHead
                            ? headBlock
                            : await _provider.GetBlockByNumAsync(num, ct);

                        // /wallet/getnowblock and /wallet/getblockbynum can omit the tx
                        // list even when txs exist; refetch explicitly in that case.
                        if ((bi.Transactions == null || bi.Transactions.Count == 0) && bi.TransactionCount > 0)
                            bi = await _provider.GetBlockByNumAsync(num, ct);

                        block = new TronBlock(
                            bi.BlockNumber,
                            bi.BlockId,
                            DateTimeOffset.FromUnixTimeMilliseconds(bi.Timestamp),
                            ConvertTransactions(bi.Transactions));
                    }
                    catch (OperationCanceledException) { yield break; }
                    catch (Exception ex)
                    {
                        // Don't advance lastBlockNumber — retry this block next interval.
                        _logger.LogDebug(ex, "Failed to fetch block {BlockNumber}, will retry", num);
                    }

                    if (block == null) break;

                    yield return block;
                    lastBlockNumber = num;
                }
            }

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
