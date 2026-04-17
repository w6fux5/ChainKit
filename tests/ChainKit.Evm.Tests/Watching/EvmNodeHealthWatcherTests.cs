using System.Text.Json;
using ChainKit.Evm.Providers;
using ChainKit.Evm.Watching;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace ChainKit.Evm.Tests.Watching;

public class EvmNodeHealthWatcherTests
{
    private readonly IEvmProvider _provider = Substitute.For<IEvmProvider>();
    private readonly EvmNetworkConfig _network = new("https://rpc", 1L, "Ethereum", "ETH");

    private static JsonElement BlockWithTimestamp(long unixSeconds)
    {
        var json = $"{{\"timestamp\":\"0x{unixSeconds:x}\"}}";
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    // ── Task 6: Happy path ──────────────────────────────────────────────────

    [Fact]
    public async Task OnHealthChecked_FiresAfterFirstPoll_WithBlockData()
    {
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(999L);
        _provider.GetBlockByNumberAsync(999L, false, Arg.Any<CancellationToken>())
            .Returns((JsonElement?)BlockWithTimestamp(nowSeconds - 3));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(1L);

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 50);
        var tcs = new TaskCompletionSource<EvmNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => tcs.TrySetResult(e.Report);

        await watcher.StartAsync();
        var report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(report.Reachable);
        Assert.Null(report.Error);
        Assert.Equal(999L, report.BlockNumber);
        Assert.NotNull(report.BlockAge);
        Assert.True(report.BlockAge >= TimeSpan.FromSeconds(2), $"BlockAge should be ~3s given block timestamp 3s ago, got {report.BlockAge}");
        Assert.True(report.ChainIdMatch, "ChainId 1 should match network ChainId 1");
    }

    // ── Task 7: ChainId semantics + caching ────────────────────────────────

    [Fact]
    public async Task ChainIdMatch_True_WhenNodeChainIdMatchesConfig()
    {
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _provider.GetBlockByNumberAsync(Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns((JsonElement?)BlockWithTimestamp(nowSeconds));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(1L);

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 50);
        var tcs = new TaskCompletionSource<EvmNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => tcs.TrySetResult(e.Report);

        await watcher.StartAsync();
        var report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(report.ChainIdMatch);
    }

    [Fact]
    public async Task ChainIdMatch_False_WhenNodeChainIdDiffers()
    {
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _provider.GetBlockByNumberAsync(Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns((JsonElement?)BlockWithTimestamp(nowSeconds));
        // Node reports Polygon (137) but config says Ethereum (1)
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(137L);

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 50);
        var tcs = new TaskCompletionSource<EvmNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => tcs.TrySetResult(e.Report);

        await watcher.StartAsync();
        var report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(report.ChainIdMatch);
    }

    [Fact]
    public async Task ChainIdMatch_CachedAfterFirstSuccess()
    {
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _provider.GetBlockByNumberAsync(Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns((JsonElement?)BlockWithTimestamp(nowSeconds));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(1L);

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 30);
        var reportCount = 0;
        watcher.OnHealthChecked += (_, _) => Interlocked.Increment(ref reportCount);

        await watcher.StartAsync();
        // Wait for at least 3 polls
        await Task.Delay(300);
        await watcher.StopAsync();

        Assert.True(reportCount >= 2, $"Expected at least 2 polls, got {reportCount}");
        // GetChainIdAsync should be called exactly once across all polls
        await _provider.Received(1).GetChainIdAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChainIdMatch_Null_BeforeFirstSuccessfulFetch()
    {
        // All provider calls throw — node is unreachable
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 50);
        var tcs = new TaskCompletionSource<EvmNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => tcs.TrySetResult(e.Report);

        await watcher.StartAsync();
        var report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(report.Reachable);
        Assert.Null(report.ChainIdMatch);
    }

    // ── Task 8: Resilience + lifecycle ─────────────────────────────────────

    [Fact]
    public async Task OnHealthChecked_ProviderThrows_ReportShowsUnreachable()
    {
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 50);
        var tcs = new TaskCompletionSource<EvmNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => tcs.TrySetResult(e.Report);

        await watcher.StartAsync();
        var report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(report.Reachable);
        Assert.Null(report.BlockNumber);
        Assert.Null(report.BlockAge);
        Assert.NotNull(report.Error);
        Assert.Contains("connection refused", report.Error);
    }

    [Fact]
    public async Task OnHealthChecked_AfterFailure_KeepsPolling()
    {
        var callCount = 0;
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                    throw new HttpRequestException("fail");
                return Task.FromResult(500L);
            });
        _provider.GetBlockByNumberAsync(Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns((JsonElement?)BlockWithTimestamp(nowSeconds));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(1L);

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 30);
        var reports = new List<EvmNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) =>
        {
            lock (reports) reports.Add(e.Report);
        };

        await watcher.StartAsync();
        await Task.Delay(300);
        await watcher.StopAsync();

        List<EvmNodeHealthReport> snapshot;
        lock (reports) snapshot = [.. reports];

        Assert.True(snapshot.Count >= 2, $"Expected at least 2 reports, got {snapshot.Count}");
        Assert.False(snapshot[0].Reachable);
        Assert.Contains(snapshot, r => r.Reachable);
    }

    [Fact]
    public async Task OnHealthChecked_HandlerThrows_DoesNotStopPolling()
    {
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _provider.GetBlockByNumberAsync(Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns((JsonElement?)BlockWithTimestamp(nowSeconds));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(1L);

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 30);
        var count = 0;
        watcher.OnHealthChecked += (_, _) =>
        {
            Interlocked.Increment(ref count);
            throw new InvalidOperationException("handler blew up");
        };

        await watcher.StartAsync();
        await Task.Delay(300);
        await watcher.StopAsync();

        Assert.True(count >= 2, $"Expected at least 2 handler invocations, got {count}");
    }

    [Fact]
    public async Task StopAsync_StopsPolling()
    {
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _provider.GetBlockByNumberAsync(Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns((JsonElement?)BlockWithTimestamp(nowSeconds));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(1L);

        var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 20);
        var count = 0;
        watcher.OnHealthChecked += (_, _) => Interlocked.Increment(ref count);

        await watcher.StartAsync();
        await Task.Delay(150);
        await watcher.StopAsync();
        var snapshotAfterStop = count;

        await Task.Delay(200);
        Assert.Equal(snapshotAfterStop, count);
    }

    [Fact]
    public async Task StartAsync_Twice_DoesNotDoubleStart()
    {
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _provider.GetBlockByNumberAsync(Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns((JsonElement?)BlockWithTimestamp(nowSeconds));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(1L);

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 50);
        await watcher.StartAsync();
        await watcher.StartAsync();
        await Task.Delay(120);
        await watcher.StopAsync();
    }

    [Fact]
    public async Task StopAsync_BeforeStart_IsNoOp()
    {
        var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 50);
        var ex = await Record.ExceptionAsync(() => watcher.StopAsync());
        Assert.Null(ex);
    }

    [Fact]
    public async Task DisposeAsync_CancelsGracefully()
    {
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _provider.GetBlockByNumberAsync(Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns((JsonElement?)BlockWithTimestamp(nowSeconds));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(1L);

        var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 20);
        await watcher.StartAsync();
        await Task.Delay(80);

        var disposeTask = watcher.DisposeAsync().AsTask();
        var finished = await Task.WhenAny(disposeTask, Task.Delay(2000));
        Assert.Same(disposeTask, finished);
    }

    [Fact]
    public async Task BlockAge_FutureBlockTimestamp_ClampedToZero()
    {
        var futureSeconds = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds();
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _provider.GetBlockByNumberAsync(Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns((JsonElement?)BlockWithTimestamp(futureSeconds));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(1L);

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 50);
        var tcs = new TaskCompletionSource<EvmNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => tcs.TrySetResult(e.Report);

        await watcher.StartAsync();
        var report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.Zero, report.BlockAge);
    }

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new EvmNodeHealthWatcher(null!, _network));
        Assert.Equal("provider", ex.ParamName);
    }

    [Fact]
    public void Constructor_NullNetwork_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new EvmNodeHealthWatcher(_provider, null!));
        Assert.Equal("network", ex.ParamName);
    }
}
