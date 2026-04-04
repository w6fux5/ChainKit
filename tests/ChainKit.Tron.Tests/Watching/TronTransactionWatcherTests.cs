using System.Numerics;
using System.Runtime.CompilerServices;
using NSubstitute;
using ChainKit.Tron.Crypto;
using ChainKit.Tron.Models;
using ChainKit.Tron.Providers;
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
    private static ITronProvider MockProvider() => Substitute.For<ITronProvider>();

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
        Assert.Throws<ArgumentNullException>(() => new TronTransactionWatcher(null!, MockProvider()));
    }

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TronTransactionWatcher(new MockBlockStream(), null!));
    }

    [Fact]
    public async Task WatchAddress_TrxReceived_FiresEvent()
    {
        var block = MakeBlock(1, MakeTrxTx(OtherAddr, WatchedAddr));
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream, MockProvider());

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
        await using var watcher = new TronTransactionWatcher(stream, MockProvider());

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
        await using var watcher = new TronTransactionWatcher(stream, MockProvider());

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
        await using var watcher = new TronTransactionWatcher(stream, MockProvider());

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
        await using var watcher = new TronTransactionWatcher(stream, MockProvider());

        int fireCount = 0;
        watcher.OnTrxReceived += (_, _) => fireCount++;
        watcher.OnTrxSent += (_, _) => fireCount++;
        watcher.OnTrc20Received += (_, _) => fireCount++;
        watcher.OnTrc20Sent += (_, _) => fireCount++;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public async Task StartAsync_StopAsync_Lifecycle()
    {
        // A stream that never ends — use cancellation to stop
        var neverEndingBlocks = new TronBlock[0]; // empty = finishes immediately
        var stream = new MockBlockStream(neverEndingBlocks);
        await using var watcher = new TronTransactionWatcher(stream, MockProvider());

        await watcher.StartAsync();
        await watcher.StopAsync();
        // Should not throw or hang
    }

    [Fact]
    public async Task DisposeAsync_CleansUp()
    {
        var stream = new MockBlockStream();
        var watcher = new TronTransactionWatcher(stream, MockProvider());

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
        await using var watcher = new TronTransactionWatcher(stream, MockProvider());

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
        await using var watcher = new TronTransactionWatcher(stream, MockProvider());

        Trc20ReceivedEventArgs? received = null;
        watcher.OnTrc20Received += (_, e) => received = e;

        // Watch the contract address (which is the "to" in the TronBlockTransaction)
        watcher.WatchAddress(contractAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.NotNull(received);
        Assert.Equal("trc20tx", received!.TxId);
        Assert.Equal(contractAddr, received.ContractAddress);
        // RawAmount is always present
        Assert.Equal((decimal)tokenAmount, received.RawAmount);
        // No provider supplied and unknown token — Amount is null
        Assert.Null(received.Amount);
        Assert.Equal(0, received.Decimals);
    }

    [Fact]
    public async Task Trc20Received_WithProvider_ResolvesSymbolAndConvertsAmount()
    {
        var provider = Substitute.For<ITronProvider>();

        // Known USDT contract address
        var contractAddr = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
        long tokenAmount = 20_200_000; // 20.2 USDT (6 decimals)

        var tx = MakeTrc20TxWithData(OtherAddr, contractAddr, WatchedAddr, tokenAmount, "usdt_tx");
        var block = MakeBlock(1, tx);
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream, provider);

        Trc20ReceivedEventArgs? received = null;
        watcher.OnTrc20Received += (_, e) => received = e;

        watcher.WatchAddress(contractAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.NotNull(received);
        Assert.Equal("usdt_tx", received!.TxId);
        Assert.Equal("USDT", received.Symbol);
        // RawAmount is always the on-chain value
        Assert.Equal((decimal)tokenAmount, received.RawAmount);
        // 20_200_000 / 10^6 = 20.2
        Assert.Equal(20.2m, received.Amount);
        Assert.Equal(6, received.Decimals);

        // Provider should NOT have been called for symbol/decimals (known token)
        await provider.DidNotReceive().TriggerConstantContractAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Trc20Received_WithProvider_UnknownToken_ResolvesViaContract()
    {
        var provider = Substitute.For<ITronProvider>();
        var contractAddr = "41" + new string('b', 40);
        long tokenAmount = 5_000_000_000_000_000_000; // 5.0 with 18 decimals

        // Mock symbol() return
        var symbolBytes = BuildAbiString("WETH");
        provider.TriggerConstantContractAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is<string>(s => s == "symbol()"),
                Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(symbolBytes);

        // Mock decimals() return = 18
        var decimalsBytes = AbiEncoder.EncodeUint256(new BigInteger(18));
        provider.TriggerConstantContractAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is<string>(s => s == "decimals()"),
                Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(decimalsBytes);

        var tx = MakeTrc20TxWithData(OtherAddr, contractAddr, WatchedAddr, tokenAmount, "weth_tx");
        var block = MakeBlock(1, tx);
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream, provider);

        Trc20ReceivedEventArgs? received = null;
        watcher.OnTrc20Received += (_, e) => received = e;

        watcher.WatchAddress(contractAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.NotNull(received);
        Assert.Equal("WETH", received!.Symbol);
        Assert.Equal((decimal)tokenAmount, received.RawAmount);
        Assert.Equal(5m, received.Amount); // 5e18 / 10^18 = 5.0
        Assert.Equal(18, received.Decimals);
    }

    [Fact]
    public async Task FromAddress_TrxSent_FiresEvent()
    {
        var block = MakeBlock(1, MakeTrxTxWithAmount(WatchedAddr, UnrelatedAddr, 5_000_000));
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream, MockProvider());

        TrxSentEventArgs? sent = null;
        watcher.OnTrxSent += (_, e) => sent = e;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.NotNull(sent);
        Assert.Equal("tx1", sent!.TxId);
        Assert.Equal(WatchedAddr, sent.FromAddress);
        Assert.Equal(UnrelatedAddr, sent.ToAddress);
        Assert.Equal(5m, sent.Amount);
    }

    [Fact]
    public async Task FromAddress_TrxSent_DoesNotFireReceived()
    {
        var block = MakeBlock(1, MakeTrxTxWithAmount(WatchedAddr, UnrelatedAddr, 5_000_000));
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream, MockProvider());

        TrxReceivedEventArgs? received = null;
        watcher.OnTrxReceived += (_, e) => received = e;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.Null(received);
    }

    [Fact]
    public async Task FromAddress_Trc20Sent_FiresEvent()
    {
        var contractAddr = "41" + new string('e', 40);
        long tokenAmount = 500_000_000;
        var tx = MakeTrc20TxWithData(WatchedAddr, contractAddr, UnrelatedAddr, tokenAmount, "trc20out");
        var block = MakeBlock(1, tx);
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream, MockProvider());

        Trc20SentEventArgs? sent = null;
        watcher.OnTrc20Sent += (_, e) => sent = e;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.NotNull(sent);
        Assert.Equal("trc20out", sent!.TxId);
        Assert.Equal(WatchedAddr, sent.FromAddress);
        Assert.Equal(contractAddr, sent.ContractAddress);
        Assert.Equal((decimal)tokenAmount, sent.RawAmount);
    }

    [Fact]
    public async Task SelfTransfer_FiresBothReceivedAndSent()
    {
        var block = MakeBlock(1, MakeTrxTxWithAmount(WatchedAddr, WatchedAddr, 1_000_000));
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream, MockProvider());

        TrxReceivedEventArgs? received = null;
        TrxSentEventArgs? sent = null;
        watcher.OnTrxReceived += (_, e) => received = e;
        watcher.OnTrxSent += (_, e) => sent = e;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.NotNull(received);
        Assert.NotNull(sent);
        Assert.Equal("tx1", received!.TxId);
        Assert.Equal("tx1", sent!.TxId);
    }

    // --- Confirmation tracker tests ---

    [Fact]
    public async Task ConfirmationTracker_TrxConfirmed_FiresEvent()
    {
        var provider = Substitute.For<ITronProvider>();
        var tx = MakeTrxTxWithAmount(OtherAddr, WatchedAddr, 10_000_000);
        var block = MakeBlock(1, tx);
        var stream = new MockBlockStream(block);

        // Solidity Node confirms immediately
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        provider.GetTransactionInfoByIdAsync("tx1", Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto("tx1", 1, ts, "", 0, 0, 0, ReceiptResult: "SUCCESS"));

        await using var watcher = new TronTransactionWatcher(stream, provider,
            confirmationIntervalMs: 50);

        var tcs = new TaskCompletionSource<TransactionConfirmedEventArgs>();
        watcher.OnTransactionConfirmed += (_, e) => tcs.TrySetResult(e);

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();

        var confirmed = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("tx1", confirmed.TxId);
        Assert.Equal(1, confirmed.BlockNumber);
    }

    [Fact]
    public async Task ConfirmationTracker_DelayedConfirmation_EventuallyFires()
    {
        var provider = Substitute.For<ITronProvider>();
        var tx = MakeTrxTxWithAmount(OtherAddr, WatchedAddr, 10_000_000);
        var block = MakeBlock(1, tx);
        var stream = new MockBlockStream(block);

        int callCount = 0;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        provider.GetTransactionInfoByIdAsync("tx1", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref callCount) <= 2)
                    return Task.FromResult(new TransactionInfoDto("", 0, 0, "", 0, 0, 0));
                return Task.FromResult(new TransactionInfoDto("tx1", 1, ts, "", 0, 0, 0,
                    ReceiptResult: "SUCCESS"));
            });

        await using var watcher = new TronTransactionWatcher(stream, provider,
            confirmationIntervalMs: 50);

        var tcs = new TaskCompletionSource<TransactionConfirmedEventArgs>();
        watcher.OnTransactionConfirmed += (_, e) => tcs.TrySetResult(e);

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();

        var confirmed = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("tx1", confirmed.TxId);
        Assert.True(callCount >= 3);
    }

    [Fact]
    public async Task ConfirmationTracker_SelfTransfer_ConfirmsOnce()
    {
        var provider = Substitute.For<ITronProvider>();
        var tx = MakeTrxTxWithAmount(WatchedAddr, WatchedAddr, 1_000_000);
        var block = MakeBlock(1, tx);
        var stream = new MockBlockStream(block);

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        provider.GetTransactionInfoByIdAsync("tx1", Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto("tx1", 1, ts, "", 0, 0, 0, ReceiptResult: "SUCCESS"));

        await using var watcher = new TronTransactionWatcher(stream, provider,
            confirmationIntervalMs: 50);

        var confirmedCount = 0;
        watcher.OnTransactionConfirmed += (_, _) => Interlocked.Increment(ref confirmedCount);

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(500);

        Assert.Equal(1, confirmedCount);
    }

    /// <summary>
    /// Builds an ABI-encoded string return value (offset + length + data padded to 32 bytes).
    /// </summary>
    private static byte[] BuildAbiString(string value)
    {
        var strBytes = System.Text.Encoding.UTF8.GetBytes(value);
        var paddedLen = ((strBytes.Length + 31) / 32) * 32;
        var result = new byte[32 + 32 + paddedLen];
        result[31] = 0x20;
        var lenBytes = AbiEncoder.EncodeUint256(new BigInteger(strBytes.Length));
        Buffer.BlockCopy(lenBytes, 0, result, 32, 32);
        Buffer.BlockCopy(strBytes, 0, result, 64, strBytes.Length);
        return result;
    }
}
