using ChainKit.Tron.Models;
using ChainKit.Tron.Providers;

namespace ChainKit.Tron.Watching;

public class PollingBlockStream : ITronBlockStream
{
    private readonly ITronProvider _provider;
    private readonly int _intervalMs;

    public PollingBlockStream(ITronProvider provider, int intervalMs = 3000)
    {
        _provider = provider;
        _intervalMs = intervalMs;
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
                    lastBlockNumber = blockInfo.BlockNumber;
                    // Convert BlockInfo to TronBlock
                    // For now, transactions list is empty since BlockInfo doesn't include tx details
                    // In a real implementation, we'd fetch full block with transactions
                    block = new TronBlock(
                        blockInfo.BlockNumber,
                        blockInfo.BlockId,
                        DateTimeOffset.FromUnixTimeMilliseconds(blockInfo.Timestamp),
                        Array.Empty<TronBlockTransaction>());
                }
            }
            catch (OperationCanceledException) { yield break; }
            catch { /* swallow provider errors, retry next interval */ }

            if (block != null)
                yield return block;

            try { await Task.Delay(_intervalMs, ct); }
            catch (OperationCanceledException) { yield break; }
        }
    }
}
