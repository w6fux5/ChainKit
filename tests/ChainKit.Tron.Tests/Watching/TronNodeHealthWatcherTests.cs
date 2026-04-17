using ChainKit.Tron.Models;
using ChainKit.Tron.Providers;
using ChainKit.Tron.Watching;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace ChainKit.Tron.Tests.Watching;

public class TronNodeHealthWatcherTests
{
    private readonly ITronProvider _provider = Substitute.For<ITronProvider>();

    private static BlockInfo CreateBlock(long number, long timestampMs)
        => new(number, "00000000000000" + number.ToString("X16"), timestampMs, 0, new byte[34]);

    [Fact]
    public async Task OnHealthChecked_FiresAfterFirstPoll()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(CreateBlock(1000, nowMs - 2000));

        await using var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 50);

        var tcs = new TaskCompletionSource<TronNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => tcs.TrySetResult(e.Report);

        await watcher.StartAsync();
        var report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(report.Reachable);
        Assert.Null(report.Error);
        Assert.Equal(1000L, report.BlockNumber);
        Assert.NotNull(report.BlockAge);
        Assert.True(report.BlockAge >= TimeSpan.FromSeconds(1), "BlockAge should be ~2s given block timestamp 2s ago");
    }

    [Fact]
    public async Task OnHealthChecked_ProviderThrows_ReportShowsUnreachable()
    {
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        await using var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 50);
        var tcs = new TaskCompletionSource<TronNodeHealthReport>();
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
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref callCount);
                if (callCount == 1) throw new HttpRequestException("fail");
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return CreateBlock(500, nowMs);
            });

        await using var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 30);
        var reports = new List<TronNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) =>
        {
            lock (reports) reports.Add(e.Report);
        };

        await watcher.StartAsync();
        await Task.Delay(300);
        await watcher.StopAsync();

        List<TronNodeHealthReport> snapshot;
        lock (reports) snapshot = [.. reports];

        Assert.True(snapshot.Count >= 2, $"Expected at least 2 reports, got {snapshot.Count}");
        Assert.False(snapshot[0].Reachable);
        Assert.Contains(snapshot, r => r.Reachable);
    }

    [Fact]
    public async Task OnHealthChecked_HandlerThrows_DoesNotStopPolling()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(CreateBlock(1, nowMs));

        await using var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 30);
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
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(CreateBlock(1, nowMs));

        var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 20);
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
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(CreateBlock(1, nowMs));

        await using var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 50);
        await watcher.StartAsync();
        await watcher.StartAsync();
        await Task.Delay(120);
        await watcher.StopAsync();
    }

    [Fact]
    public async Task StopAsync_BeforeStart_IsNoOp()
    {
        var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 50);
        var ex = await Record.ExceptionAsync(() => watcher.StopAsync());
        Assert.Null(ex);
    }

    [Fact]
    public async Task DisposeAsync_CancelsGracefully()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(CreateBlock(1, nowMs));

        var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 20);
        await watcher.StartAsync();
        await Task.Delay(80);

        var disposeTask = watcher.DisposeAsync().AsTask();
        var finished = await Task.WhenAny(disposeTask, Task.Delay(2000));
        Assert.Same(disposeTask, finished);
    }

    [Fact]
    public async Task OnHealthChecked_FiresRepeatedlyAtInterval()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(CreateBlock(1, nowMs));

        await using var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 30);
        var count = 0;
        watcher.OnHealthChecked += (_, _) => Interlocked.Increment(ref count);

        await watcher.StartAsync();
        await Task.Delay(300);
        await watcher.StopAsync();

        Assert.True(count >= 3, $"Expected at least 3 periodic fires, got {count}");
    }

    [Fact]
    public async Task BlockAge_ComputedRelativeToUtcNow()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(CreateBlock(1, nowMs - 5000));

        await using var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 50);
        var tcs = new TaskCompletionSource<TronNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => tcs.TrySetResult(e.Report);

        await watcher.StartAsync();
        var report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(report.BlockAge);
        Assert.True(report.BlockAge >= TimeSpan.FromSeconds(4), $"BlockAge={report.BlockAge}");
        Assert.True(report.BlockAge < TimeSpan.FromSeconds(10), $"BlockAge={report.BlockAge}");
    }

    [Fact]
    public async Task BlockAge_FutureBlockTimestamp_ClampedToZero()
    {
        var futureMs = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeMilliseconds();
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(CreateBlock(1, futureMs));

        await using var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 50);
        var tcs = new TaskCompletionSource<TronNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => tcs.TrySetResult(e.Report);

        await watcher.StartAsync();
        var report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.Zero, report.BlockAge);
    }

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TronNodeHealthWatcher(null!));
    }
}
