using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NSubstitute;
using ChainKit.Evm.Models;
using ChainKit.Evm.Providers;
using ChainKit.Evm.Watching;
using Xunit;

namespace ChainKit.Evm.Tests.Watching;

/// <summary>
/// Mock block stream that yields a fixed set of blocks then completes.
/// </summary>
internal class MockEvmBlockStream : IEvmBlockStream
{
    private readonly EvmBlock[] _blocks;

    public MockEvmBlockStream(params EvmBlock[] blocks) { _blocks = blocks; }

    public async IAsyncEnumerable<EvmBlock> GetBlocksAsync(long startBlock,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var block in _blocks)
        {
            if (ct.IsCancellationRequested) yield break;
            yield return block;
            await Task.Yield();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public class EvmTransactionWatcherTests
{
    private const string WatchedAddr = "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string OtherAddr = "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string UnrelatedAddr = "0xcccccccccccccccccccccccccccccccccccccccc";
    private const string Erc20Contract = "0xdddddddddddddddddddddddddddddddddddddddd";

    private static readonly EvmNetworkConfig TestNetwork = new("http://localhost:8545", 1, "Test", "ETH");

    private static IEvmProvider MockProvider() => Substitute.For<IEvmProvider>();

    private static EvmBlock MakeBlock(long num, params EvmBlockTransaction[] txs) =>
        new(num, $"0xhash{num}", DateTimeOffset.UtcNow, txs.ToList());

    private static EvmBlockTransaction MakeNativeTx(string from, string to,
        BigInteger value, string txHash = "0xtx1") =>
        new(txHash, from, to, value, Array.Empty<byte>(), null);

    /// <summary>
    /// Builds a mock receipt JSON with ERC-20 Transfer logs.
    /// </summary>
    private static JsonElement MakeReceiptWithTransferLog(
        string contractAddr, string from, string to, BigInteger amount,
        string status = "0x1", long blockNumber = 1)
    {
        var fromTopic = "0x" + new string('0', 24) + from[2..].ToLowerInvariant();
        var toTopic = "0x" + new string('0', 24) + to[2..].ToLowerInvariant();
        var amountHex = amount.ToString("x64");
        var blockNumHex = "0x" + blockNumber.ToString("x");

        var json = $$"""
        {
            "status": "{{status}}",
            "blockNumber": "{{blockNumHex}}",
            "logs": [
                {
                    "address": "{{contractAddr}}",
                    "topics": [
                        "0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef",
                        "{{fromTopic}}",
                        "{{toTopic}}"
                    ],
                    "data": "0x{{amountHex}}"
                }
            ]
        }
        """;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Builds a simple receipt JSON without logs.
    /// </summary>
    private static JsonElement MakeSimpleReceipt(string status = "0x1", long blockNumber = 1)
    {
        var blockNumHex = "0x" + blockNumber.ToString("x");
        var json = $$"""{"status":"{{status}}","blockNumber":"{{blockNumHex}}","logs":[]}""";
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    // --- Constructor tests ---

    [Fact]
    public void Constructor_NullBlockStream_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EvmTransactionWatcher(null!, MockProvider(), TestNetwork));
    }

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EvmTransactionWatcher(new MockEvmBlockStream(), null!, TestNetwork));
    }

    [Fact]
    public void Constructor_NullNetwork_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EvmTransactionWatcher(new MockEvmBlockStream(), MockProvider(), null!));
    }

    // --- Native transfer events ---

    [Fact]
    public async Task NativeReceived_FiresForWatchedAddress()
    {
        var oneEth = BigInteger.Parse("1000000000000000000"); // 1 ETH
        var block = MakeBlock(1, MakeNativeTx(OtherAddr, WatchedAddr, oneEth));
        var stream = new MockEvmBlockStream(block);
        var provider = MockProvider();

        // Receipt for the ERC-20 log detection pass (no logs in this case)
        provider.GetTransactionReceiptAsync("0xtx1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JsonElement?>(MakeSimpleReceipt()));

        await using var watcher = new EvmTransactionWatcher(stream, provider, TestNetwork);

        NativeReceivedEventArgs? received = null;
        watcher.OnNativeReceived += (_, e) => received = e;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync(startBlock: 1);
        await Task.Delay(200);

        Assert.NotNull(received);
        Assert.Equal("0xtx1", received!.TxId);
        Assert.Equal(OtherAddr, received.FromAddress);
        Assert.Equal(WatchedAddr, received.ToAddress);
        Assert.Equal(1m, received.Amount); // 1 ETH
        Assert.Equal(oneEth, received.RawAmount);
    }

    [Fact]
    public async Task NativeSent_FiresForWatchedSender()
    {
        var halfEth = BigInteger.Parse("500000000000000000"); // 0.5 ETH
        var block = MakeBlock(1, MakeNativeTx(WatchedAddr, OtherAddr, halfEth));
        var stream = new MockEvmBlockStream(block);
        var provider = MockProvider();

        provider.GetTransactionReceiptAsync("0xtx1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JsonElement?>(MakeSimpleReceipt()));

        await using var watcher = new EvmTransactionWatcher(stream, provider, TestNetwork);

        NativeSentEventArgs? sent = null;
        watcher.OnNativeSent += (_, e) => sent = e;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync(startBlock: 1);
        await Task.Delay(200);

        Assert.NotNull(sent);
        Assert.Equal("0xtx1", sent!.TxId);
        Assert.Equal(WatchedAddr, sent.FromAddress);
        Assert.Equal(OtherAddr, sent.ToAddress);
        Assert.Equal(0.5m, sent.Amount);
    }

    [Fact]
    public async Task NativeSent_DoesNotFireReceived()
    {
        var block = MakeBlock(1, MakeNativeTx(WatchedAddr, UnrelatedAddr, BigInteger.One));
        var stream = new MockEvmBlockStream(block);
        var provider = MockProvider();

        provider.GetTransactionReceiptAsync("0xtx1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JsonElement?>(MakeSimpleReceipt()));

        await using var watcher = new EvmTransactionWatcher(stream, provider, TestNetwork);

        NativeReceivedEventArgs? received = null;
        watcher.OnNativeReceived += (_, e) => received = e;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync(startBlock: 1);
        await Task.Delay(200);

        Assert.Null(received);
    }

    [Fact]
    public async Task ZeroValue_DoesNotFireNativeEvents()
    {
        var block = MakeBlock(1, MakeNativeTx(OtherAddr, WatchedAddr, BigInteger.Zero));
        var stream = new MockEvmBlockStream(block);
        var provider = MockProvider();

        await using var watcher = new EvmTransactionWatcher(stream, provider, TestNetwork);

        int fireCount = 0;
        watcher.OnNativeReceived += (_, _) => fireCount++;
        watcher.OnNativeSent += (_, _) => fireCount++;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync(startBlock: 1);
        await Task.Delay(200);

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public async Task SelfTransfer_FiresBothReceivedAndSent()
    {
        var oneEth = BigInteger.Parse("1000000000000000000");
        var block = MakeBlock(1, MakeNativeTx(WatchedAddr, WatchedAddr, oneEth));
        var stream = new MockEvmBlockStream(block);
        var provider = MockProvider();

        provider.GetTransactionReceiptAsync("0xtx1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JsonElement?>(MakeSimpleReceipt()));

        await using var watcher = new EvmTransactionWatcher(stream, provider, TestNetwork);

        NativeReceivedEventArgs? received = null;
        NativeSentEventArgs? sent = null;
        watcher.OnNativeReceived += (_, e) => received = e;
        watcher.OnNativeSent += (_, e) => sent = e;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync(startBlock: 1);
        await Task.Delay(200);

        Assert.NotNull(received);
        Assert.NotNull(sent);
        Assert.Equal("0xtx1", received!.TxId);
        Assert.Equal("0xtx1", sent!.TxId);
    }

    // --- ERC-20 Transfer log detection ---

    [Fact]
    public async Task Erc20Received_FiresForWatchedRecipient()
    {
        // Build a tx with ERC-20 transfer call data
        var transferData = BuildErc20TransferInput(WatchedAddr, 1_000_000);
        var tx = new EvmBlockTransaction("0xerc20tx", OtherAddr, Erc20Contract, BigInteger.Zero, transferData, null);
        var block = MakeBlock(1, tx);
        var stream = new MockEvmBlockStream(block);
        var provider = MockProvider();

        // Return receipt with Transfer log
        var receipt = MakeReceiptWithTransferLog(Erc20Contract, OtherAddr, WatchedAddr, new BigInteger(1_000_000));
        provider.GetTransactionReceiptAsync("0xerc20tx", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JsonElement?>(receipt));

        await using var watcher = new EvmTransactionWatcher(stream, provider, TestNetwork);

        Erc20ReceivedEventArgs? received = null;
        watcher.OnErc20Received += (_, e) => received = e;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync(startBlock: 1);
        await Task.Delay(200);

        Assert.NotNull(received);
        Assert.Equal("0xerc20tx", received!.TxId);
        Assert.Equal(Erc20Contract.ToLowerInvariant(), received.ContractAddress.ToLowerInvariant());
        Assert.Equal(new BigInteger(1_000_000), received.RawAmount);
    }

    [Fact]
    public async Task Erc20Sent_FiresForWatchedSender()
    {
        var transferData = BuildErc20TransferInput(OtherAddr, 2_000_000);
        var tx = new EvmBlockTransaction("0xerc20tx", WatchedAddr, Erc20Contract, BigInteger.Zero, transferData, null);
        var block = MakeBlock(1, tx);
        var stream = new MockEvmBlockStream(block);
        var provider = MockProvider();

        var receipt = MakeReceiptWithTransferLog(Erc20Contract, WatchedAddr, OtherAddr, new BigInteger(2_000_000));
        provider.GetTransactionReceiptAsync("0xerc20tx", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JsonElement?>(receipt));

        await using var watcher = new EvmTransactionWatcher(stream, provider, TestNetwork);

        Erc20SentEventArgs? sent = null;
        watcher.OnErc20Sent += (_, e) => sent = e;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync(startBlock: 1);
        await Task.Delay(200);

        Assert.NotNull(sent);
        Assert.Equal("0xerc20tx", sent!.TxId);
        Assert.Equal(new BigInteger(2_000_000), sent.RawAmount);
    }

    // --- Unwatched address filtering ---

    [Fact]
    public async Task UnrelatedTransaction_DoesNotFireEvents()
    {
        var block = MakeBlock(1, MakeNativeTx(UnrelatedAddr, OtherAddr, BigInteger.Parse("1000000000000000000")));
        var stream = new MockEvmBlockStream(block);
        var provider = MockProvider();

        await using var watcher = new EvmTransactionWatcher(stream, provider, TestNetwork);

        int fireCount = 0;
        watcher.OnNativeReceived += (_, _) => fireCount++;
        watcher.OnNativeSent += (_, _) => fireCount++;
        watcher.OnErc20Received += (_, _) => fireCount++;
        watcher.OnErc20Sent += (_, _) => fireCount++;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync(startBlock: 1);
        await Task.Delay(200);

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public async Task UnwatchAddress_StopsEvents()
    {
        var oneEth = BigInteger.Parse("1000000000000000000");
        var block = MakeBlock(1, MakeNativeTx(OtherAddr, WatchedAddr, oneEth));
        var stream = new MockEvmBlockStream(block);
        var provider = MockProvider();

        await using var watcher = new EvmTransactionWatcher(stream, provider, TestNetwork);

        int fireCount = 0;
        watcher.OnNativeReceived += (_, _) => fireCount++;

        watcher.WatchAddress(WatchedAddr);
        watcher.UnwatchAddress(WatchedAddr);
        await watcher.StartAsync(startBlock: 1);
        await Task.Delay(200);

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public async Task WatchAddresses_Multiple_FiresForBoth()
    {
        var oneEth = BigInteger.Parse("1000000000000000000");
        var tx1 = MakeNativeTx(UnrelatedAddr, WatchedAddr, oneEth, "0xtxA");
        var tx2 = MakeNativeTx(UnrelatedAddr, OtherAddr, oneEth, "0xtxB");
        var block = MakeBlock(1, tx1, tx2);
        var stream = new MockEvmBlockStream(block);
        var provider = MockProvider();

        provider.GetTransactionReceiptAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JsonElement?>(MakeSimpleReceipt()));

        await using var watcher = new EvmTransactionWatcher(stream, provider, TestNetwork);

        var receivedTxIds = new List<string>();
        watcher.OnNativeReceived += (_, e) => receivedTxIds.Add(e.TxId);

        watcher.WatchAddresses(new[] { WatchedAddr, OtherAddr });
        await watcher.StartAsync(startBlock: 1);
        await Task.Delay(200);

        Assert.Contains("0xtxA", receivedTxIds);
        Assert.Contains("0xtxB", receivedTxIds);
    }

    // --- Confirmation events ---

    [Fact]
    public async Task Confirmation_FiresAfterSufficientBlocks()
    {
        var oneEth = BigInteger.Parse("1000000000000000000");
        var block = MakeBlock(100, MakeNativeTx(OtherAddr, WatchedAddr, oneEth));
        var stream = new MockEvmBlockStream(block);
        var provider = MockProvider();

        provider.GetTransactionReceiptAsync("0xtx1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JsonElement?>(MakeSimpleReceipt(blockNumber: 100)));
        provider.GetBlockNumberAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(115L)); // 115 - 100 >= 12 confirmations

        await using var watcher = new EvmTransactionWatcher(stream, provider, TestNetwork,
            confirmationBlocks: 12, confirmationIntervalMs: 50);

        var tcs = new TaskCompletionSource<TransactionConfirmedEventArgs>();
        watcher.OnTransactionConfirmed += (_, e) => tcs.TrySetResult(e);

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync(startBlock: 100);

        var confirmed = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("0xtx1", confirmed.TxId);
        Assert.Equal(100, confirmed.BlockNumber);
    }

    [Fact]
    public async Task Confirmation_NotEnoughBlocks_DoesNotFire()
    {
        var oneEth = BigInteger.Parse("1000000000000000000");
        var block = MakeBlock(100, MakeNativeTx(OtherAddr, WatchedAddr, oneEth));
        var stream = new MockEvmBlockStream(block);
        var provider = MockProvider();

        provider.GetTransactionReceiptAsync("0xtx1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JsonElement?>(MakeSimpleReceipt(blockNumber: 100)));
        provider.GetBlockNumberAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(105L)); // 105 - 100 = 5 < 12 confirmations

        await using var watcher = new EvmTransactionWatcher(stream, provider, TestNetwork,
            confirmationBlocks: 12, confirmationIntervalMs: 50);

        var confirmed = false;
        watcher.OnTransactionConfirmed += (_, _) => confirmed = true;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync(startBlock: 100);
        await Task.Delay(300);

        Assert.False(confirmed);
    }

    [Fact]
    public async Task FailedTransaction_FiresFailedEvent()
    {
        var oneEth = BigInteger.Parse("1000000000000000000");
        var block = MakeBlock(100, MakeNativeTx(OtherAddr, WatchedAddr, oneEth));
        var stream = new MockEvmBlockStream(block);
        var provider = MockProvider();

        // Return receipt with status 0x0 (failed/reverted)
        provider.GetTransactionReceiptAsync("0xtx1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JsonElement?>(MakeSimpleReceipt(status: "0x0", blockNumber: 100)));
        provider.GetBlockNumberAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(100L));

        await using var watcher = new EvmTransactionWatcher(stream, provider, TestNetwork,
            confirmationIntervalMs: 50);

        var tcs = new TaskCompletionSource<TransactionFailedEventArgs>();
        watcher.OnTransactionFailed += (_, e) => tcs.TrySetResult(e);

        var confirmedFired = false;
        watcher.OnTransactionConfirmed += (_, _) => confirmedFired = true;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync(startBlock: 100);

        var failed = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("0xtx1", failed.TxId);
        Assert.Contains("reverted", failed.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(confirmedFired);
    }

    [Fact]
    public async Task Confirmation_Expired_FiresFailed()
    {
        var oneEth = BigInteger.Parse("1000000000000000000");
        var block = MakeBlock(100, MakeNativeTx(OtherAddr, WatchedAddr, oneEth));
        var stream = new MockEvmBlockStream(block);
        var provider = MockProvider();

        // Receipt never found
        provider.GetTransactionReceiptAsync("0xtx1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JsonElement?>(null));
        provider.GetBlockNumberAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(100L));

        await using var watcher = new EvmTransactionWatcher(stream, provider, TestNetwork,
            confirmationIntervalMs: 50, maxPendingAge: TimeSpan.FromMilliseconds(200));

        var tcs = new TaskCompletionSource<TransactionFailedEventArgs>();
        watcher.OnTransactionFailed += (_, e) => tcs.TrySetResult(e);

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync(startBlock: 100);

        var failed = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("0xtx1", failed.TxId);
        Assert.Contains("timed out", failed.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Confirmation_DelayedReceipt_EventuallyFires()
    {
        var oneEth = BigInteger.Parse("1000000000000000000");
        var block = MakeBlock(50, MakeNativeTx(OtherAddr, WatchedAddr, oneEth));
        var stream = new MockEvmBlockStream(block);
        var provider = MockProvider();

        int callCount = 0;
        provider.GetTransactionReceiptAsync("0xtx1", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref callCount) <= 2)
                    return Task.FromResult<JsonElement?>(null); // not yet mined
                return Task.FromResult<JsonElement?>(MakeSimpleReceipt(blockNumber: 50));
            });
        provider.GetBlockNumberAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(70L)); // 70 - 50 >= 12

        await using var watcher = new EvmTransactionWatcher(stream, provider, TestNetwork,
            confirmationBlocks: 12, confirmationIntervalMs: 50);

        var tcs = new TaskCompletionSource<TransactionConfirmedEventArgs>();
        watcher.OnTransactionConfirmed += (_, e) => tcs.TrySetResult(e);

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync(startBlock: 50);

        var confirmed = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("0xtx1", confirmed.TxId);
        Assert.True(callCount >= 3);
    }

    // --- Lifecycle tests ---

    [Fact]
    public async Task StartAsync_StopAsync_Lifecycle()
    {
        var stream = new MockEvmBlockStream(); // empty, finishes immediately
        var provider = MockProvider();

        await using var watcher = new EvmTransactionWatcher(stream, provider, TestNetwork);
        await watcher.StartAsync(startBlock: 0);
        await watcher.StopAsync();
        // Should not throw or hang
    }

    [Fact]
    public async Task DisposeAsync_CleansUp()
    {
        var stream = new MockEvmBlockStream();
        var watcher = new EvmTransactionWatcher(stream, MockProvider(), TestNetwork);

        await watcher.StartAsync(startBlock: 0);
        await watcher.DisposeAsync();
        // Should not throw or hang
    }

    [Fact]
    public async Task StopAsync_ClearsPending_NoResidualEvents()
    {
        var oneEth = BigInteger.Parse("1000000000000000000");
        var block = MakeBlock(1, MakeNativeTx(OtherAddr, WatchedAddr, oneEth));
        var stream = new MockEvmBlockStream(block);
        var provider = MockProvider();

        provider.GetTransactionReceiptAsync("0xtx1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JsonElement?>(null));
        provider.GetBlockNumberAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1L));

        var watcher = new EvmTransactionWatcher(stream, provider, TestNetwork,
            confirmationIntervalMs: 50);

        var confirmedCount = 0;
        var failedCount = 0;
        watcher.OnTransactionConfirmed += (_, _) => Interlocked.Increment(ref confirmedCount);
        watcher.OnTransactionFailed += (_, _) => Interlocked.Increment(ref failedCount);

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync(startBlock: 1);
        await Task.Delay(100);

        await watcher.StopAsync();
        await Task.Delay(200);

        Assert.Equal(0, confirmedCount);
        Assert.Equal(0, failedCount);

        await watcher.DisposeAsync();
    }

    // --- Static helper tests ---

    [Fact]
    public void ExtractAddressFromTopic_ValidTopic_ReturnsAddress()
    {
        var topic = "0x000000000000000000000000aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var addr = EvmTransactionWatcher.ExtractAddressFromTopic(topic);
        Assert.Equal("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", addr);
    }

    [Fact]
    public void ExtractAddressFromTopic_EmptyTopic_ReturnsEmpty()
    {
        Assert.Equal("", EvmTransactionWatcher.ExtractAddressFromTopic(""));
        Assert.Equal("", EvmTransactionWatcher.ExtractAddressFromTopic(null!));
    }

    [Fact]
    public void ExtractAddressFromTopic_ShortTopic_ReturnsEmpty()
    {
        Assert.Equal("", EvmTransactionWatcher.ExtractAddressFromTopic("0x1234"));
    }

    [Fact]
    public void ExtractAddressFromTopic_WithoutPrefix_StillWorks()
    {
        var topic = "000000000000000000000000bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var addr = EvmTransactionWatcher.ExtractAddressFromTopic(topic);
        Assert.Equal("0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", addr);
    }

    [Fact]
    public void TransferTopic_IsCorrectKeccak256()
    {
        // Transfer(address,address,uint256) keccak hash
        Assert.Equal("0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef",
            EvmTransactionWatcher.TransferTopic);
    }

    // --- Helper to build ERC-20 transfer call data ---

    /// <summary>
    /// Builds ABI-encoded transfer(address,uint256) call data.
    /// Selector: a9059cbb
    /// </summary>
    private static byte[] BuildErc20TransferInput(string toAddress, long amount)
    {
        var result = new byte[68]; // 4 selector + 32 address + 32 amount

        // Function selector: transfer(address,uint256) = a9059cbb
        result[0] = 0xa9;
        result[1] = 0x05;
        result[2] = 0x9c;
        result[3] = 0xbb;

        // Address parameter (left-padded to 32 bytes)
        var addrHex = toAddress.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? toAddress[2..] : toAddress;
        var addrBytes = Convert.FromHexString(addrHex);
        Buffer.BlockCopy(addrBytes, 0, result, 4 + 32 - addrBytes.Length, addrBytes.Length);

        // Amount parameter (left-padded to 32 bytes)
        var amountBytes = new BigInteger(amount).ToByteArray(isUnsigned: true, isBigEndian: true);
        Buffer.BlockCopy(amountBytes, 0, result, 36 + 32 - amountBytes.Length, amountBytes.Length);

        return result;
    }
}
