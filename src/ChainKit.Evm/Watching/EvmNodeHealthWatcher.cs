using ChainKit.Evm.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChainKit.Evm.Watching;

/// <summary>
/// Periodically probes the configured EVM node and raises <see cref="OnHealthChecked"/>
/// events with raw metrics. The watcher reports; the caller decides what "healthy" means.
/// </summary>
public sealed class EvmNodeHealthWatcher : IAsyncDisposable
{
    private readonly IEvmProvider _provider;
    private readonly EvmNetworkConfig _network;
    private readonly int _intervalMs;
    private readonly ILogger<EvmNodeHealthWatcher> _logger;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private long? _cachedChainId;

    /// <summary>Raised after every poll, whether it succeeded or failed.</summary>
    public event EventHandler<EvmNodeHealthCheckedEventArgs>? OnHealthChecked;

    /// <summary>
    /// Creates a new health watcher. Call <see cref="StartAsync"/> to begin polling.
    /// </summary>
    /// <param name="provider">The EVM provider to probe.</param>
    /// <param name="network">The network configuration, used to validate chain ID.</param>
    /// <param name="intervalMs">Interval between polls in milliseconds. Default 5000.</param>
    /// <param name="logger">Optional logger. Defaults to NullLogger.</param>
    public EvmNodeHealthWatcher(
        IEvmProvider provider,
        EvmNetworkConfig network,
        int intervalMs = 5000,
        ILogger<EvmNodeHealthWatcher>? logger = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _intervalMs = intervalMs;
        _logger = logger ?? NullLogger<EvmNodeHealthWatcher>.Instance;
    }

    /// <summary>Starts the polling loop. Subsequent calls are no-ops while running.</summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        if (_loopTask is not null) return Task.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = Task.Run(() => PollLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    /// <summary>Stops the polling loop. Safe to call multiple times; safe to call before Start.</summary>
    public async Task StopAsync()
    {
        if (_cts is null) return;
        _cts.Cancel();
        try
        {
            if (_loopTask is not null) await _loopTask;
        }
        catch (OperationCanceledException) { /* expected */ }
        _cts.Dispose();
        _cts = null;
        _loopTask = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var report = await ProbeAsync(ct);
            try
            {
                OnHealthChecked?.Invoke(this, new EvmNodeHealthCheckedEventArgs(report));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OnHealthChecked handler threw; continuing polling");
            }

            try
            {
                await Task.Delay(_intervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<EvmNodeHealthReport> ProbeAsync(CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var blockNumber = await _provider.GetBlockNumberAsync(ct);
            var block = await _provider.GetBlockByNumberAsync(blockNumber, false, ct);
            var now = DateTimeOffset.UtcNow;
            var latency = now - startedAt;

            // Fetch chain ID once; cache result; retry on next poll if it fails
            if (_cachedChainId is null)
            {
                try
                {
                    _cachedChainId = await _provider.GetChainIdAsync(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "GetChainIdAsync failed; will retry next poll");
                }
            }

            bool? chainIdMatch = _cachedChainId.HasValue
                ? _cachedChainId.Value == _network.ChainId
                : null;

            TimeSpan? blockAge = null;
            if (block.HasValue)
            {
                var timestampHex = block.Value.GetProperty("timestamp").GetString()
                    ?? throw new InvalidOperationException("Block timestamp is null");
                var timestampSeconds = Convert.ToInt64(timestampHex, 16);
                var blockTime = DateTimeOffset.FromUnixTimeSeconds(timestampSeconds);
                var age = now - blockTime;
                blockAge = age < TimeSpan.Zero ? TimeSpan.Zero : age;
            }

            return new EvmNodeHealthReport(
                Timestamp: now,
                Reachable: true,
                Latency: latency,
                BlockNumber: blockNumber,
                BlockAge: blockAge,
                ChainIdMatch: chainIdMatch,
                Error: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var now = DateTimeOffset.UtcNow;
            return new EvmNodeHealthReport(
                Timestamp: now,
                Reachable: false,
                Latency: now - startedAt,
                BlockNumber: null,
                BlockAge: null,
                ChainIdMatch: null,
                Error: ex.Message);
        }
    }
}
