using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using ChainKit.Core.Converters;
using ChainKit.Evm.Contracts;
using ChainKit.Evm.Models;
using ChainKit.Evm.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChainKit.Evm.Watching;

/// <summary>
/// Watches EVM blocks for native and ERC-20 token transfers involving watched addresses.
/// Fires six events: OnNativeReceived/Sent, OnErc20Received/Sent, OnTransactionConfirmed/Failed.
/// Follows TronTransactionWatcher architecture with three-stage lifecycle (Start/Stop/Dispose).
/// </summary>
public sealed class EvmTransactionWatcher : IAsyncDisposable
{
    /// <summary>
    /// Keccak-256 hash of Transfer(address,address,uint256) event signature.
    /// </summary>
    internal static readonly string TransferTopic = "0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef";

    private readonly IEvmBlockStream _blockStream;
    private readonly IEvmProvider _provider;
    private readonly EvmNetworkConfig _network;
    private readonly TokenInfoCache? _tokenCache;
    private readonly ILogger<EvmTransactionWatcher> _logger;
    private readonly HashSet<string> _watchedAddresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PendingTx> _unconfirmedTxs = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _confirmationBlocks;
    private readonly int _confirmationIntervalMs;
    private readonly TimeSpan _maxPendingAge;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _watchTask;
    private Task? _confirmTask;

    internal record PendingTx(string TxId, long BlockNumber, DateTimeOffset DiscoveredAt);

    /// <summary>Fires when a native transfer TO a watched address is found in a block.</summary>
    public event EventHandler<NativeReceivedEventArgs>? OnNativeReceived;

    /// <summary>Fires when a native transfer FROM a watched address is found in a block.</summary>
    public event EventHandler<NativeSentEventArgs>? OnNativeSent;

    /// <summary>Fires when an ERC-20 Transfer log TO a watched address is found in a block.</summary>
    public event EventHandler<Erc20ReceivedEventArgs>? OnErc20Received;

    /// <summary>Fires when an ERC-20 Transfer log FROM a watched address is found in a block.</summary>
    public event EventHandler<Erc20SentEventArgs>? OnErc20Sent;

    /// <summary>Fires when a pending transaction is confirmed (receipt status 0x1 + sufficient block depth).</summary>
    public event EventHandler<TransactionConfirmedEventArgs>? OnTransactionConfirmed;

    /// <summary>Fires when a pending transaction fails (receipt status 0x0) or times out.</summary>
    public event EventHandler<TransactionFailedEventArgs>? OnTransactionFailed;

    /// <summary>
    /// Creates a new EvmTransactionWatcher instance.
    /// </summary>
    /// <param name="blockStream">The block stream to consume.</param>
    /// <param name="provider">The EVM provider for receipt/log queries.</param>
    /// <param name="network">The network configuration (for chain ID in token cache).</param>
    /// <param name="tokenCache">Optional token info cache for ERC-20 metadata resolution.</param>
    /// <param name="confirmationBlocks">Number of blocks required for confirmation. Defaults to 12.</param>
    /// <param name="confirmationIntervalMs">Interval between confirmation checks in milliseconds. Defaults to 5000.</param>
    /// <param name="maxPendingAge">Maximum age before a pending tx is considered expired. Defaults to 10 minutes.</param>
    /// <param name="logger">Optional logger. Defaults to NullLogger.</param>
    public EvmTransactionWatcher(IEvmBlockStream blockStream, IEvmProvider provider,
        EvmNetworkConfig network, TokenInfoCache? tokenCache = null,
        int confirmationBlocks = 12, int confirmationIntervalMs = 5000,
        TimeSpan? maxPendingAge = null,
        ILogger<EvmTransactionWatcher>? logger = null)
    {
        _blockStream = blockStream ?? throw new ArgumentNullException(nameof(blockStream));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _tokenCache = tokenCache;
        _confirmationBlocks = confirmationBlocks;
        _confirmationIntervalMs = confirmationIntervalMs;
        _maxPendingAge = maxPendingAge ?? TimeSpan.FromMinutes(10);
        _logger = logger ?? NullLogger<EvmTransactionWatcher>.Instance;
    }

    /// <summary>
    /// Adds an address to the watch list. Addresses are compared case-insensitively.
    /// </summary>
    /// <param name="address">The address to watch (0x-prefixed).</param>
    public void WatchAddress(string address)
    {
        lock (_lock) _watchedAddresses.Add(address);
    }

    /// <summary>
    /// Adds multiple addresses to the watch list.
    /// </summary>
    /// <param name="addresses">The addresses to watch.</param>
    public void WatchAddresses(IEnumerable<string> addresses)
    {
        lock (_lock)
        {
            foreach (var addr in addresses)
                _watchedAddresses.Add(addr);
        }
    }

    /// <summary>
    /// Removes an address from the watch list.
    /// </summary>
    /// <param name="address">The address to stop watching.</param>
    public void UnwatchAddress(string address)
    {
        lock (_lock) _watchedAddresses.Remove(address);
    }

    /// <summary>
    /// Starts the watch and confirmation loops.
    /// </summary>
    /// <param name="startBlock">The block number to start watching from. If null, defaults to 0.</param>
    /// <param name="ct">External cancellation token.</param>
    public Task StartAsync(long? startBlock = null, CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var start = startBlock ?? 0;
        _watchTask = WatchLoopAsync(start, _cts.Token);
        _confirmTask = ConfirmationLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the watch and confirmation loops and clears pending transactions.
    /// </summary>
    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_watchTask != null)
        {
            try { await _watchTask; }
            catch (OperationCanceledException) { }
        }
        if (_confirmTask != null)
        {
            try { await _confirmTask; }
            catch (OperationCanceledException) { }
        }
        _unconfirmedTxs.Clear();
    }

    private async Task WatchLoopAsync(long startBlock, CancellationToken ct)
    {
        await foreach (var block in _blockStream.GetBlocksAsync(startBlock, ct))
        {
            foreach (var tx in block.Transactions)
                await ProcessTransactionAsync(tx, block, ct);
        }
    }

    private async Task ProcessTransactionAsync(EvmBlockTransaction tx, EvmBlock block, CancellationToken ct)
    {
        bool fromWatched, toWatched;
        lock (_lock)
        {
            fromWatched = _watchedAddresses.Contains(tx.From);
            toWatched = !string.IsNullOrEmpty(tx.To) && _watchedAddresses.Contains(tx.To);
        }

        // Native transfer detection (value > 0)
        if (tx.Value > BigInteger.Zero && (fromWatched || toWatched))
        {
            var amount = TokenConverter.TryToTokenAmount(tx.Value, _network.Decimals);

            if (toWatched)
            {
                OnNativeReceived?.Invoke(this, new NativeReceivedEventArgs(
                    tx.TxHash, tx.From, tx.To, amount, tx.Value));
            }
            if (fromWatched)
            {
                OnNativeSent?.Invoke(this, new NativeSentEventArgs(
                    tx.TxHash, tx.From, tx.To, amount, tx.Value));
            }
        }

        var hasErc20Log = HasWatchedErc20Log(tx);

        // ERC-20 Transfer log detection via receipt logs
        if (fromWatched || toWatched || hasErc20Log)
        {
            await DetectErc20TransfersAsync(tx, ct);
        }

        // Track for confirmation if any watched address is involved
        if (fromWatched || toWatched || hasErc20Log)
        {
            _unconfirmedTxs.TryAdd(tx.TxHash,
                new PendingTx(tx.TxHash, block.BlockNumber, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>
    /// Checks if the transaction input data looks like an ERC-20 transfer call
    /// and if any watched address might be involved (by checking the input data for
    /// the transfer selector). Full detection happens via receipt logs.
    /// </summary>
    private bool HasWatchedErc20Log(EvmBlockTransaction tx)
    {
        // transfer(address,uint256) selector = a9059cbb
        if (tx.Input.Length >= 68
            && tx.Input[0] == 0xa9 && tx.Input[1] == 0x05
            && tx.Input[2] == 0x9c && tx.Input[3] == 0xbb)
        {
            // Extract the recipient address from the ABI-encoded parameter
            var recipientBytes = tx.Input[16..36]; // last 20 bytes of 32-byte address param
            var recipientAddr = "0x" + Convert.ToHexString(recipientBytes).ToLowerInvariant();
            lock (_lock)
            {
                return _watchedAddresses.Contains(recipientAddr)
                    || _watchedAddresses.Contains(tx.From);
            }
        }
        return false;
    }

    /// <summary>
    /// Fetches the transaction receipt and parses Transfer event logs to fire ERC-20 events.
    /// </summary>
    private async Task DetectErc20TransfersAsync(EvmBlockTransaction tx, CancellationToken ct)
    {
        JsonElement? receipt;
        try
        {
            receipt = await _provider.GetTransactionReceiptAsync(tx.TxHash, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to fetch receipt for {TxHash}", tx.TxHash);
            return;
        }

        if (receipt == null) return;

        if (!receipt.Value.TryGetProperty("logs", out var logsEl) || logsEl.ValueKind != JsonValueKind.Array)
            return;

        foreach (var log in logsEl.EnumerateArray())
        {
            if (!log.TryGetProperty("topics", out var topicsEl) || topicsEl.ValueKind != JsonValueKind.Array)
                continue;

            var topics = topicsEl.EnumerateArray().ToList();
            if (topics.Count < 3) continue;

            var topic0 = topics[0].GetString() ?? "";
            if (!string.Equals(topic0, TransferTopic, StringComparison.OrdinalIgnoreCase))
                continue;

            // Parse Transfer(address indexed from, address indexed to, uint256 value)
            var fromAddr = ExtractAddressFromTopic(topics[1].GetString() ?? "");
            var toAddr = ExtractAddressFromTopic(topics[2].GetString() ?? "");
            var contractAddr = log.TryGetProperty("address", out var addrEl) ? addrEl.GetString() ?? "" : "";

            var rawAmount = BigInteger.Zero;
            if (log.TryGetProperty("data", out var dataEl) && dataEl.GetString() is string dataHex)
            {
                var cleanData = dataHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? dataHex[2..] : dataHex;
                if (cleanData.Length > 0)
                    rawAmount = BigInteger.Parse("0" + cleanData, NumberStyles.HexNumber);
            }

            bool erc20FromWatched, erc20ToWatched;
            lock (_lock)
            {
                erc20FromWatched = _watchedAddresses.Contains(fromAddr);
                erc20ToWatched = _watchedAddresses.Contains(toAddr);
            }

            if (!erc20FromWatched && !erc20ToWatched) continue;

            // Resolve token info
            string? symbol = null;
            decimal? convertedAmount = null;
            if (_tokenCache != null)
            {
                try
                {
                    var tokenInfo = await _tokenCache.GetOrResolveAsync(
                        contractAddr, _network.ChainId,
                        async addr =>
                        {
                            var contract = new Erc20Contract(_provider, addr, _network);
                            var result = await contract.GetTokenInfoAsync(ct);
                            return result.Success ? result.Data : null;
                        }, ct);
                    if (tokenInfo != null)
                    {
                        symbol = tokenInfo.Symbol;
                        if (tokenInfo.Decimals > 0)
                            convertedAmount = TokenConverter.TryToTokenAmount(rawAmount, tokenInfo.Decimals);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Token info resolution failed for {Contract}", contractAddr);
                }
            }

            if (erc20ToWatched)
            {
                OnErc20Received?.Invoke(this, new Erc20ReceivedEventArgs(
                    tx.TxHash, contractAddr, fromAddr, toAddr,
                    rawAmount, convertedAmount, symbol));
            }
            if (erc20FromWatched)
            {
                OnErc20Sent?.Invoke(this, new Erc20SentEventArgs(
                    tx.TxHash, contractAddr, fromAddr, toAddr,
                    rawAmount, convertedAmount, symbol));
            }

            // Also track ERC-20 tx for confirmation
            _unconfirmedTxs.TryAdd(tx.TxHash,
                new PendingTx(tx.TxHash, 0, DateTimeOffset.UtcNow));
        }
    }

    private async Task ConfirmationLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(_confirmationIntervalMs, ct); }
            catch (OperationCanceledException) { return; }

            long currentBlock;
            try { currentBlock = await _provider.GetBlockNumberAsync(ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get current block number");
                continue;
            }

            foreach (var kvp in _unconfirmedTxs)
            {
                if (ct.IsCancellationRequested) break;

                var pending = kvp.Value;

                // Check expiry
                if (DateTimeOffset.UtcNow - pending.DiscoveredAt > _maxPendingAge)
                {
                    if (_unconfirmedTxs.TryRemove(kvp.Key, out _))
                    {
                        OnTransactionFailed?.Invoke(this, new TransactionFailedEventArgs(
                            pending.TxId, "Transaction confirmation timed out"));
                    }
                    continue;
                }

                try
                {
                    var receipt = await _provider.GetTransactionReceiptAsync(pending.TxId, ct);
                    if (receipt == null) continue; // not yet mined

                    // Get the block number from receipt
                    long txBlock = pending.BlockNumber;
                    if (receipt.Value.TryGetProperty("blockNumber", out var bnEl)
                        && bnEl.GetString() is string bnStr
                        && bnStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        txBlock = Convert.ToInt64(bnStr[2..], 16);
                    }

                    // Check receipt status
                    var status = receipt.Value.TryGetProperty("status", out var statusEl)
                        ? statusEl.GetString() : null;

                    if (status == "0x0")
                    {
                        // Transaction reverted
                        if (_unconfirmedTxs.TryRemove(kvp.Key, out _))
                        {
                            OnTransactionFailed?.Invoke(this, new TransactionFailedEventArgs(
                                pending.TxId, "Transaction reverted"));
                        }
                        continue;
                    }

                    // Check block depth for confirmation
                    if (currentBlock - txBlock >= _confirmationBlocks)
                    {
                        if (_unconfirmedTxs.TryRemove(kvp.Key, out _))
                        {
                            OnTransactionConfirmed?.Invoke(this, new TransactionConfirmedEventArgs(
                                pending.TxId, txBlock));
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Confirmation check failed for tx {TxId}, retrying next cycle", pending.TxId);
                }
            }
        }
    }

    /// <summary>
    /// Extracts a 0x-prefixed address from a 32-byte hex topic (last 20 bytes).
    /// Example: "0x000000000000000000000000abcdef..." -> "0xabcdef..."
    /// </summary>
    internal static string ExtractAddressFromTopic(string topic)
    {
        if (string.IsNullOrEmpty(topic)) return "";
        var clean = topic.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? topic[2..] : topic;
        if (clean.Length < 40) return "";
        // Last 40 hex chars = 20 bytes = address
        return "0x" + clean[^40..].ToLowerInvariant();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }
}
