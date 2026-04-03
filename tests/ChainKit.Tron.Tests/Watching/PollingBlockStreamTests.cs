using NSubstitute;
using ChainKit.Tron.Models;
using ChainKit.Tron.Providers;
using ChainKit.Tron.Watching;
using Xunit;

namespace ChainKit.Tron.Tests.Watching;

public class PollingBlockStreamTests
{
    [Fact]
    public async Task StreamBlocksAsync_YieldsNewBlocks()
    {
        var provider = Substitute.For<ITronProvider>();
        var blockNum = 0L;
        provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new BlockInfo(++blockNum, $"block{blockNum}",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, Array.Empty<byte>()));

        var stream = new PollingBlockStream(provider, intervalMs: 50);
        var cts = new CancellationTokenSource(500); // stop after 500ms
        var blocks = new List<TronBlock>();

        await foreach (var block in stream.StreamBlocksAsync(cts.Token))
            blocks.Add(block);

        Assert.True(blocks.Count >= 2);
        Assert.Equal(1, blocks[0].BlockNumber);
    }

    [Fact]
    public async Task StreamBlocksAsync_SkipsDuplicateBlocks()
    {
        var provider = Substitute.For<ITronProvider>();
        // Always return same block number
        provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(100, "same", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, Array.Empty<byte>()));

        var stream = new PollingBlockStream(provider, intervalMs: 50);
        var cts = new CancellationTokenSource(300);
        var blocks = new List<TronBlock>();

        await foreach (var block in stream.StreamBlocksAsync(cts.Token))
            blocks.Add(block);

        Assert.Single(blocks); // only 1 unique block
    }

    [Fact]
    public async Task StreamBlocksAsync_HandlesProviderErrors()
    {
        var provider = Substitute.For<ITronProvider>();
        var callCount = 0;
        provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 1) throw new HttpRequestException("timeout");
                return new BlockInfo(1, "b1", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, Array.Empty<byte>());
            });

        var stream = new PollingBlockStream(provider, intervalMs: 50);
        var cts = new CancellationTokenSource(300);
        var blocks = new List<TronBlock>();

        await foreach (var block in stream.StreamBlocksAsync(cts.Token))
            blocks.Add(block);

        Assert.NotEmpty(blocks); // recovered after error
    }

    [Fact]
    public async Task StreamBlocksAsync_RespectsCancellation()
    {
        var provider = Substitute.For<ITronProvider>();
        provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(1, "b1", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, Array.Empty<byte>()));

        var stream = new PollingBlockStream(provider, intervalMs: 10000); // long interval
        var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel immediately

        var blocks = new List<TronBlock>();
        await foreach (var block in stream.StreamBlocksAsync(cts.Token))
            blocks.Add(block);

        // Should complete quickly without hanging
    }
}
