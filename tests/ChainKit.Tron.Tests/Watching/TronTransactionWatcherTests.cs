using System.Runtime.CompilerServices;
using ChainKit.Tron.Crypto;
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

    /// <summary>
    /// Builds RawData with a TRX amount in the PollingBlockStream format.
    /// Layout: [8 bytes big-endian amount][4: 0 contractAddr len][4: 0 data len]
    /// </summary>
    private static byte[] BuildTrxRawData(long amountSun)
    {
        var txInfo = new BlockTransactionInfo("", "TransferContract", "", "", amountSun, null, null);
        return PollingBlockStream.BuildRawData(txInfo);
    }

    /// <summary>
    /// Builds RawData with TRC20 transfer data in the PollingBlockStream format.
    /// </summary>
    private static byte[] BuildTrc20RawData(string contractAddress, string recipientHex, long tokenAmount)
    {
        // Build ABI-encoded transfer(address,uint256) call data
        var callData = AbiEncoder.EncodeTransfer(recipientHex, new System.Numerics.BigInteger(tokenAmount));
        var txInfo = new BlockTransactionInfo("", "TriggerSmartContract", "", "",
            0, contractAddress, callData);
        return PollingBlockStream.BuildRawData(txInfo);
    }

    private static TronBlockTransaction MakeTrxTxWithAmount(
        string from, string to, long amountSun, string txId = "tx1") =>
        new(txId, from, to, "TransferContract", BuildTrxRawData(amountSun));

    private static TronBlockTransaction MakeTrc20TxWithData(
        string from, string contractAddr, string recipientHex, long tokenAmount, string txId = "tx2") =>
        new(txId, from, contractAddr, "TriggerSmartContract",
            BuildTrc20RawData(contractAddr, recipientHex, tokenAmount));

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

    // --- Amount parsing tests ---

    [Fact]
    public void ParseTrxAmount_PollingFormat_ReturnsCorrectTrx()
    {
        var rawData = BuildTrxRawData(5_000_000); // 5 TRX
        var amount = TronTransactionWatcher.ParseTrxAmount(rawData);
        Assert.Equal(5m, amount);
    }

    [Fact]
    public void ParseTrxAmount_EmptyData_ReturnsZero()
    {
        Assert.Equal(0m, TronTransactionWatcher.ParseTrxAmount(Array.Empty<byte>()));
        Assert.Equal(0m, TronTransactionWatcher.ParseTrxAmount(null!));
    }

    [Fact]
    public void ParseTrxAmount_SmallData_ReturnsZero()
    {
        Assert.Equal(0m, TronTransactionWatcher.ParseTrxAmount(new byte[4]));
    }

    [Fact]
    public void ParseTrc20Transfer_WithTransferData_ReturnsAmountAndRecipient()
    {
        var recipientHex = "41" + new string('a', 40);
        long tokenAmount = 1_000_000_000; // e.g. 1000 USDT (6 decimals)

        var rawData = BuildTrc20RawData("41contractcontractcontractcontractcontrac", recipientHex, tokenAmount);
        var info = TronTransactionWatcher.ParseTrc20Transfer(rawData);

        Assert.Equal("41contractcontractcontractcontractcontrac", info.ContractAddress);
        Assert.NotNull(info.Recipient);
        Assert.Equal((decimal)tokenAmount, info.Amount);
    }

    [Fact]
    public void ParseTrc20Transfer_EmptyData_ReturnsEmpty()
    {
        var info = TronTransactionWatcher.ParseTrc20Transfer(Array.Empty<byte>());
        Assert.Equal("", info.ContractAddress);
        Assert.Null(info.Recipient);
        Assert.Equal(0m, info.Amount);
    }

    [Fact]
    public async Task TrxReceived_WithRealAmount_EventContainsAmount()
    {
        // 10 TRX = 10_000_000 sun
        var tx = MakeTrxTxWithAmount(OtherAddr, WatchedAddr, 10_000_000);
        var block = MakeBlock(1, tx);
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream);

        TrxReceivedEventArgs? received = null;
        watcher.OnTrxReceived += (_, e) => received = e;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.NotNull(received);
        Assert.Equal(10m, received!.Amount); // 10 TRX
    }

    [Fact]
    public async Task Trc20Received_WithRealData_EventContainsAmountAndContract()
    {
        var contractAddr = "41" + new string('e', 40);
        long tokenAmount = 500_000_000;

        var tx = MakeTrc20TxWithData(OtherAddr, contractAddr, WatchedAddr, tokenAmount, "trc20tx");
        var block = MakeBlock(1, tx);
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream);

        Trc20ReceivedEventArgs? received = null;
        watcher.OnTrc20Received += (_, e) => received = e;

        // Watch the contract address (which is the "to" in the TronBlockTransaction)
        watcher.WatchAddress(contractAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.NotNull(received);
        Assert.Equal("trc20tx", received!.TxId);
        Assert.Equal(contractAddr, received.ContractAddress);
        Assert.Equal((decimal)tokenAmount, received.Amount);
    }
}
