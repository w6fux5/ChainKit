using ChainKit.Tron.Models;
using ChainKit.Tron.Providers;
using ChainKit.Tron.Watching;
using NSubstitute;
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
}
