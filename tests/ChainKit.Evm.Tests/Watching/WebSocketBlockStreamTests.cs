using NSubstitute;
using ChainKit.Evm.Providers;
using ChainKit.Evm.Watching;
using Xunit;

namespace ChainKit.Evm.Tests.Watching;

public class WebSocketBlockStreamTests
{
    [Fact]
    public void Constructor_NullWsUrl_Throws()
    {
        var provider = Substitute.For<IEvmProvider>();
        Assert.Throws<ArgumentNullException>(() => new WebSocketBlockStream(null!, provider));
    }

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WebSocketBlockStream("wss://example.com", null!));
    }

    [Fact]
    public void Constructor_ValidArgs_CreatesInstance()
    {
        var provider = Substitute.For<IEvmProvider>();
        var stream = new WebSocketBlockStream("wss://example.com", provider);
        Assert.NotNull(stream);
    }

    [Fact]
    public void ImplementsIEvmBlockStream()
    {
        var provider = Substitute.For<IEvmProvider>();
        var stream = new WebSocketBlockStream("wss://example.com", provider);
        Assert.IsAssignableFrom<IEvmBlockStream>(stream);
    }

    [Fact]
    public async Task DisposeAsync_CompletesSuccessfully()
    {
        var provider = Substitute.For<IEvmProvider>();
        var stream = new WebSocketBlockStream("wss://example.com", provider);
        await stream.DisposeAsync();
        // Should not throw
    }

    [Fact]
    public void ExtractBlockNumberFromNotification_ValidNewHead_ReturnsBlockNumber()
    {
        var notification = """{"jsonrpc":"2.0","method":"eth_subscription","params":{"subscription":"0x123","result":{"number":"0x1a2b3c"}}}""";
        var blockNum = WebSocketBlockStream.ExtractBlockNumberFromNotification(notification);
        Assert.Equal(0x1a2b3c, blockNum);
    }

    [Fact]
    public void ExtractBlockNumberFromNotification_BlockZero_ReturnsZero()
    {
        var notification = """{"jsonrpc":"2.0","method":"eth_subscription","params":{"subscription":"0x1","result":{"number":"0x0"}}}""";
        var blockNum = WebSocketBlockStream.ExtractBlockNumberFromNotification(notification);
        Assert.Equal(0, blockNum);
    }

    [Fact]
    public void ExtractBlockNumberFromNotification_LargeBlockNumber_ParsesCorrectly()
    {
        // Block 20,000,000 = 0x1312d00
        var notification = """{"jsonrpc":"2.0","method":"eth_subscription","params":{"subscription":"0x1","result":{"number":"0x1312d00"}}}""";
        var blockNum = WebSocketBlockStream.ExtractBlockNumberFromNotification(notification);
        Assert.Equal(20_000_000, blockNum);
    }

    [Fact]
    public void ExtractBlockNumberFromNotification_SubscriptionConfirmation_ReturnsNull()
    {
        // Subscription confirmation has no params.result.number
        var confirmation = """{"jsonrpc":"2.0","id":1,"result":"0xabc123"}""";
        var blockNum = WebSocketBlockStream.ExtractBlockNumberFromNotification(confirmation);
        Assert.Null(blockNum);
    }

    [Fact]
    public void ExtractBlockNumberFromNotification_InvalidJson_ReturnsNull()
    {
        var blockNum = WebSocketBlockStream.ExtractBlockNumberFromNotification("not json");
        Assert.Null(blockNum);
    }

    [Fact]
    public void ExtractBlockNumberFromNotification_EmptyString_ReturnsNull()
    {
        var blockNum = WebSocketBlockStream.ExtractBlockNumberFromNotification("");
        Assert.Null(blockNum);
    }

    [Fact]
    public void ExtractBlockNumberFromNotification_MissingParams_ReturnsNull()
    {
        var notification = """{"jsonrpc":"2.0","method":"eth_subscription"}""";
        var blockNum = WebSocketBlockStream.ExtractBlockNumberFromNotification(notification);
        Assert.Null(blockNum);
    }

    [Fact]
    public void ExtractBlockNumberFromNotification_MissingResult_ReturnsNull()
    {
        var notification = """{"jsonrpc":"2.0","method":"eth_subscription","params":{"subscription":"0x1"}}""";
        var blockNum = WebSocketBlockStream.ExtractBlockNumberFromNotification(notification);
        Assert.Null(blockNum);
    }

    [Fact]
    public void ExtractBlockNumberFromNotification_NumberWithoutHexPrefix_ReturnsNull()
    {
        var notification = """{"jsonrpc":"2.0","method":"eth_subscription","params":{"subscription":"0x1","result":{"number":"12345"}}}""";
        var blockNum = WebSocketBlockStream.ExtractBlockNumberFromNotification(notification);
        Assert.Null(blockNum);
    }

    [Fact]
    public void Constructor_CustomBackoff_AcceptsValues()
    {
        var provider = Substitute.For<IEvmProvider>();
        var stream = new WebSocketBlockStream(
            "wss://example.com", provider,
            initialBackoff: TimeSpan.FromSeconds(2),
            maxBackoff: TimeSpan.FromMinutes(1));
        Assert.NotNull(stream);
    }

    [Fact]
    public async Task GetBlocksAsync_CancelledImmediately_YieldsNothing()
    {
        var provider = Substitute.For<IEvmProvider>();
        var stream = new WebSocketBlockStream("wss://invalid.example.com", provider);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var blocks = new List<ChainKit.Evm.Models.EvmBlock>();
        try
        {
            await foreach (var block in stream.GetBlocksAsync(0, cts.Token))
                blocks.Add(block);
        }
        catch (OperationCanceledException)
        {
            // Expected when token is already cancelled
        }

        Assert.Empty(blocks);
    }
}
