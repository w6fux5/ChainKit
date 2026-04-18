using ChainKit.Tron.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChainKit.Tron.Watching;

/// <summary>
/// Periodically probes the configured Tron node and raises <see cref="OnHealthChecked"/>
/// events with raw metrics. The watcher reports; the caller decides what "healthy" means.
/// </summary>
public sealed class TronNodeHealthWatcher : IAsyncDisposable
{
    private readonly ITronProvider _provider;
    private readonly int _intervalMs;
    private readonly ILogger<TronNodeHealthWatcher> _logger;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    /// <summary>Raised after every poll, whether it succeeded or failed.</summary>
    public event EventHandler<TronNodeHealthCheckedEventArgs>? OnHealthChecked;

    /// <summary>
    /// Creates a new health watcher. Call <see cref="StartAsync"/> to begin polling.
    /// </summary>
    /// <param name="provider">The Tron provider to probe.</param>
    /// <param name="intervalMs">Interval between polls in milliseconds. Default 5000.</param>
    /// <param name="logger">Optional logger. Defaults to NullLogger.</param>
    public TronNodeHealthWatcher(
        ITronProvider provider,
        int intervalMs = 5000,
        ILogger<TronNodeHealthWatcher>? logger = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _intervalMs = intervalMs;
        _logger = logger ?? NullLogger<TronNodeHealthWatcher>.Instance;
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
                OnHealthChecked?.Invoke(this, new TronNodeHealthCheckedEventArgs(report));
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

    private async Task<TronNodeHealthReport> ProbeAsync(CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var block = await _provider.GetNowBlockAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var latency = now - startedAt;
            var blockTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(block.Timestamp);
            var blockAge = now - blockTimestamp;
            if (blockAge < TimeSpan.Zero) blockAge = TimeSpan.Zero;

            return new TronNodeHealthReport(
                Timestamp: now,
                Reachable: true,
                Latency: latency,
                BlockNumber: block.BlockNumber,
                BlockAge: blockAge,
                Error: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var now = DateTimeOffset.UtcNow;
            return new TronNodeHealthReport(
                Timestamp: now,
                Reachable: false,
                Latency: now - startedAt,
                BlockNumber: null,
                BlockAge: null,
                Error: ex.Message);
        }
    }
}
