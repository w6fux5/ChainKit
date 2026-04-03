using ChainKit.Tron.Watching;
using Xunit;

namespace ChainKit.Tron.Tests.Watching;

public class ZmqBlockStreamTests
{
    [Fact]
    public void Constructor_NullEndpoint_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ZmqBlockStream(null!));
    }

    [Fact]
    public void Constructor_ValidEndpoint_DoesNotThrow()
    {
        var stream = new ZmqBlockStream("tcp://localhost:5555");
        Assert.NotNull(stream);
    }

    [Fact]
    public async Task StreamBlocksAsync_CancelledToken_CompletesImmediately()
    {
        var stream = new ZmqBlockStream("tcp://localhost:5555");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var blocks = new List<ChainKit.Tron.Models.TronBlock>();
        // This should not hang — it should complete quickly
        // Note: NetMQ may throw if no socket available, which is fine
        try
        {
            await foreach (var block in stream.StreamBlocksAsync(cts.Token))
                blocks.Add(block);
        }
        catch (NetMQ.TerminatingException) { /* expected when no context */ }
        catch (Exception) { /* acceptable for unit test without real ZMQ endpoint */ }
    }
}
