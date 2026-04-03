using ChainKit.Tron.Models;

namespace ChainKit.Tron.Watching;

public class TronTransactionWatcher : IAsyncDisposable
{
    private readonly ITronBlockStream _stream;
    private readonly HashSet<string> _watchedAddresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _watchTask;

    public TronTransactionWatcher(ITronBlockStream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public void WatchAddress(string address)
    {
        var normalized = NormalizeAddress(address);
        lock (_lock) { _watchedAddresses.Add(normalized); }
    }

    public void WatchAddresses(IEnumerable<string> addresses)
    {
        lock (_lock)
        {
            foreach (var addr in addresses)
                _watchedAddresses.Add(NormalizeAddress(addr));
        }
    }

    public void UnwatchAddress(string address)
    {
        var normalized = NormalizeAddress(address);
        lock (_lock) { _watchedAddresses.Remove(normalized); }
    }

    public event EventHandler<TrxReceivedEventArgs>? OnTrxReceived;
    public event EventHandler<Trc20ReceivedEventArgs>? OnTrc20Received;
    public event EventHandler<TransactionConfirmedEventArgs>? OnTransactionConfirmed;

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _watchTask = WatchLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_watchTask != null)
        {
            try { await _watchTask; }
            catch (OperationCanceledException) { }
        }
    }

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        await foreach (var block in _stream.StreamBlocksAsync(ct))
        {
            foreach (var tx in block.Transactions)
            {
                bool isWatched;
                lock (_lock)
                {
                    isWatched = _watchedAddresses.Contains(tx.ToAddress)
                             || _watchedAddresses.Contains(tx.FromAddress);
                }
                if (isWatched)
                    ProcessTransaction(tx, block);
            }
        }
    }

    private void ProcessTransaction(TronBlockTransaction tx, TronBlock block)
    {
        // Determine transaction type and fire appropriate event
        if (tx.ContractType == "TransferContract")
        {
            bool toWatched;
            lock (_lock) { toWatched = _watchedAddresses.Contains(tx.ToAddress); }
            if (toWatched)
            {
                OnTrxReceived?.Invoke(this, new TrxReceivedEventArgs(
                    tx.TxId, tx.FromAddress, tx.ToAddress,
                    0m, block.BlockNumber, block.Timestamp));
            }
        }
        else if (tx.ContractType == "TriggerSmartContract")
        {
            bool toWatched;
            lock (_lock) { toWatched = _watchedAddresses.Contains(tx.ToAddress); }
            if (toWatched)
            {
                OnTrc20Received?.Invoke(this, new Trc20ReceivedEventArgs(
                    tx.TxId, tx.FromAddress, tx.ToAddress,
                    "", "", 0m, block.BlockNumber, block.Timestamp));
            }
        }

        // Always fire confirmed event for matched transactions
        OnTransactionConfirmed?.Invoke(this, new TransactionConfirmedEventArgs(
            tx.TxId, block.BlockNumber, true));
    }

    private static string NormalizeAddress(string address)
    {
        // Convert Base58 to hex for consistent comparison
        if (address.StartsWith('T'))
            return Crypto.TronAddress.ToHex(address);
        return address.ToLowerInvariant();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }
}
