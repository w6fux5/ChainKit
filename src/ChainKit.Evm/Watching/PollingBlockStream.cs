using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ChainKit.Evm.Models;
using ChainKit.Evm.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChainKit.Evm.Watching;

/// <summary>
/// Polls <see cref="IEvmProvider.GetBlockByNumberAsync"/> sequentially to produce a block stream.
/// Yields one block at a time, incrementing the block number. When caught up to the chain tip
/// (block returns null), waits <see cref="_pollInterval"/> before retrying.
/// </summary>
public sealed class PollingBlockStream : IEvmBlockStream
{
    private readonly IEvmProvider _provider;
    private readonly TimeSpan _pollInterval;
    private readonly ILogger<PollingBlockStream> _logger;

    /// <summary>
    /// Creates a new PollingBlockStream instance.
    /// </summary>
    /// <param name="provider">The EVM provider for RPC calls.</param>
    /// <param name="pollInterval">Interval between polls when caught up to the chain tip. Defaults to 3 seconds.</param>
    /// <param name="logger">Optional logger. Defaults to NullLogger.</param>
    public PollingBlockStream(IEvmProvider provider, TimeSpan? pollInterval = null,
        ILogger<PollingBlockStream>? logger = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(3);
        _logger = logger ?? NullLogger<PollingBlockStream>.Instance;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<EvmBlock> GetBlocksAsync(long startBlock,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var currentBlock = startBlock;
        while (!ct.IsCancellationRequested)
        {
            JsonElement? blockData;
            try
            {
                blockData = await _provider.GetBlockByNumberAsync(currentBlock, true, ct);
            }
            catch (OperationCanceledException) { yield break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch block {BlockNumber}, retrying...", currentBlock);
                try { await Task.Delay(_pollInterval, ct); }
                catch (OperationCanceledException) { yield break; }
                continue;
            }

            if (blockData == null)
            {
                // Caught up to chain tip, wait and retry
                try { await Task.Delay(_pollInterval, ct); }
                catch (OperationCanceledException) { yield break; }
                continue;
            }

            var block = ParseBlock(blockData.Value, currentBlock);
            yield return block;
            currentBlock++;
        }
    }

    /// <summary>
    /// Parses a JSON-RPC block response into an <see cref="EvmBlock"/>.
    /// </summary>
    internal static EvmBlock ParseBlock(JsonElement blockData, long fallbackNumber)
    {
        var blockNumber = fallbackNumber;
        if (blockData.TryGetProperty("number", out var numEl) && numEl.GetString() is string numStr)
            blockNumber = Convert.ToInt64(numStr[2..], 16);

        var blockHash = blockData.TryGetProperty("hash", out var hashEl) ? hashEl.GetString() ?? "" : "";

        var timestamp = DateTimeOffset.UtcNow;
        if (blockData.TryGetProperty("timestamp", out var tsEl) && tsEl.GetString() is string tsStr)
        {
            var unixTs = Convert.ToInt64(tsStr[2..], 16);
            timestamp = DateTimeOffset.FromUnixTimeSeconds(unixTs);
        }

        var transactions = new List<EvmBlockTransaction>();
        if (blockData.TryGetProperty("transactions", out var txArray) && txArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var tx in txArray.EnumerateArray())
            {
                if (tx.ValueKind == JsonValueKind.String) continue; // not full tx mode

                var txHash = tx.TryGetProperty("hash", out var txHashEl) ? txHashEl.GetString() ?? "" : "";
                var from = tx.TryGetProperty("from", out var fromEl) ? fromEl.GetString() ?? "" : "";
                var to = tx.TryGetProperty("to", out var toEl) && toEl.ValueKind != JsonValueKind.Null
                    ? toEl.GetString() ?? "" : "";

                var value = BigInteger.Zero;
                if (tx.TryGetProperty("value", out var valEl) && valEl.GetString() is string valHex)
                {
                    var cleanHex = valHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? valHex[2..] : valHex;
                    if (cleanHex.Length > 0)
                        value = BigInteger.Parse("0" + cleanHex, NumberStyles.HexNumber);
                }

                var input = Array.Empty<byte>();
                if (tx.TryGetProperty("input", out var inputEl) && inputEl.GetString() is string inputStr)
                {
                    var cleanInput = inputStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? inputStr[2..] : inputStr;
                    if (cleanInput.Length > 0)
                        input = Convert.FromHexString(cleanInput);
                }

                transactions.Add(new EvmBlockTransaction(txHash, from, to, value, input, null));
            }
        }

        return new EvmBlock(blockNumber, blockHash, timestamp, transactions);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
