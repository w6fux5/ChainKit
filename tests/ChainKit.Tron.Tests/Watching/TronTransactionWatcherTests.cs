using System.Runtime.CompilerServices;
using ChainKit.Tron.Models;
using ChainKit.Tron.Watching;
using Xunit;

namespace ChainKit.Tron.Tests.Watching;

internal class MockBlockStream : ITronBlockStream
{
    private readonly TronBlock[] _blocks;
    public MockBlockStream(params TronBlock[] blocks) { _blocks = blocks; }

    public async IAsyncEnumerable<TronBlock> StreamBlocksAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var block in _blocks)
        {
            if (ct.IsCancellationRequested) yield break;
            yield return block;
            await Task.Yield(); // allow event handlers to run
        }
    }
}

public class TronTransactionWatcherTests
{
    private const string WatchedAddr = "41aabbccdd00112233445566778899aabbccddeeff";
    private const string OtherAddr = "41112233445566778899aabbccddeeff00112233aa";
    private const string UnrelatedAddr = "41ffffffffffffffffffffffffffffffffffffffff";

    private static TronBlock MakeBlock(
        long num, params TronBlockTransaction[] txs) =>
        new(num, $"block{num}", DateTimeOffset.UtcNow, txs);

    private static TronBlockTransaction MakeTrxTx(
        string from, string to, string txId = "tx1") =>
        new(txId, from, to, "TransferContract", Array.Empty<byte>());

    private static TronBlockTransaction MakeTrc20Tx(
        string from, string to, string txId = "tx2") =>
        new(txId, from, to, "TriggerSmartContract", Array.Empty<byte>());

    [Fact]
    public void Constructor_NullStream_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TronTransactionWatcher(null!));
    }

    [Fact]
    public async Task WatchAddress_TrxReceived_FiresEvent()
    {
        var block = MakeBlock(1, MakeTrxTx(OtherAddr, WatchedAddr));
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream);

        TrxReceivedEventArgs? received = null;
        watcher.OnTrxReceived += (_, e) => received = e;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        // Wait for watch loop to complete (mock stream is finite)
        await Task.Delay(200);

        Assert.NotNull(received);
        Assert.Equal("tx1", received!.TxId);
        Assert.Equal(OtherAddr, received.FromAddress);
        Assert.Equal(WatchedAddr, received.ToAddress);
        Assert.Equal(1, received.BlockNumber);
    }

    [Fact]
    public async Task WatchAddress_Trc20Received_FiresEvent()
    {
        var block = MakeBlock(1, MakeTrc20Tx(OtherAddr, WatchedAddr));
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream);

        Trc20ReceivedEventArgs? received = null;
        watcher.OnTrc20Received += (_, e) => received = e;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.NotNull(received);
        Assert.Equal("tx2", received!.TxId);
        Assert.Equal(WatchedAddr, received.ToAddress);
    }

    [Fact]
    public async Task UnwatchAddress_StopsEvents()
    {
        var block = MakeBlock(1, MakeTrxTx(OtherAddr, WatchedAddr));
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream);

        int fireCount = 0;
        watcher.OnTrxReceived += (_, _) => fireCount++;

        watcher.WatchAddress(WatchedAddr);
        watcher.UnwatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public async Task WatchAddresses_MultipleAddresses_FiresForBoth()
    {
        var tx1 = MakeTrxTx(UnrelatedAddr, WatchedAddr, "txA");
        var tx2 = MakeTrxTx(UnrelatedAddr, OtherAddr, "txB");
        var block = MakeBlock(1, tx1, tx2);
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream);

        var receivedTxIds = new List<string>();
        watcher.OnTrxReceived += (_, e) => receivedTxIds.Add(e.TxId);

        watcher.WatchAddresses(new[] { WatchedAddr, OtherAddr });
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.Contains("txA", receivedTxIds);
        Assert.Contains("txB", receivedTxIds);
    }

    [Fact]
    public async Task UnrelatedTransaction_DoesNotFireEvents()
    {
        var block = MakeBlock(1, MakeTrxTx(UnrelatedAddr, UnrelatedAddr));
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream);

        int fireCount = 0;
        watcher.OnTrxReceived += (_, _) => fireCount++;
        watcher.OnTrc20Received += (_, _) => fireCount++;
        watcher.OnTransactionConfirmed += (_, _) => fireCount++;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public async Task OnTransactionConfirmed_FiresForAllMatchedTransactions()
    {
        var tx1 = MakeTrxTx(OtherAddr, WatchedAddr, "txA");
        var tx2 = MakeTrc20Tx(OtherAddr, WatchedAddr, "txB");
        var block = MakeBlock(5, tx1, tx2);
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream);

        var confirmed = new List<TransactionConfirmedEventArgs>();
        watcher.OnTransactionConfirmed += (_, e) => confirmed.Add(e);

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.Equal(2, confirmed.Count);
        Assert.Contains(confirmed, c => c.TxId == "txA" && c.BlockNumber == 5 && c.Success);
        Assert.Contains(confirmed, c => c.TxId == "txB" && c.BlockNumber == 5 && c.Success);
    }

    [Fact]
    public async Task FromAddress_AlsoMatchesWatchedAddress()
    {
        // When the watched address is the sender
        var block = MakeBlock(1, MakeTrxTx(WatchedAddr, UnrelatedAddr));
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream);

        var confirmed = new List<TransactionConfirmedEventArgs>();
        watcher.OnTransactionConfirmed += (_, e) => confirmed.Add(e);

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        // Confirmed fires for any matched tx (from or to)
        Assert.Single(confirmed);
    }

    [Fact]
    public async Task StartAsync_StopAsync_Lifecycle()
    {
        // A stream that never ends — use cancellation to stop
        var neverEndingBlocks = new TronBlock[0]; // empty = finishes immediately
        var stream = new MockBlockStream(neverEndingBlocks);
        await using var watcher = new TronTransactionWatcher(stream);

        await watcher.StartAsync();
        await watcher.StopAsync();
        // Should not throw or hang
    }

    [Fact]
    public async Task DisposeAsync_CleansUp()
    {
        var stream = new MockBlockStream();
        var watcher = new TronTransactionWatcher(stream);

        await watcher.StartAsync();
        await watcher.DisposeAsync();
        // Should not throw or hang
    }
}
