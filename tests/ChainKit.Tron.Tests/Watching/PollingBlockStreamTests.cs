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

    [Fact]
    public async Task StreamBlocksAsync_IncludesTransactionsFromBlockInfo()
    {
        var provider = Substitute.For<ITronProvider>();
        var txs = new List<BlockTransactionInfo>
        {
            new("txhash1", "TransferContract", "41aabbccdd00112233445566778899aabbccddeeff",
                "41112233445566778899aabbccddeeff00112233aa", 5_000_000, null, null),
        };
        provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(1, "b1", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 1, Array.Empty<byte>(), txs));

        var stream = new PollingBlockStream(provider, intervalMs: 50);
        var cts = new CancellationTokenSource(300);
        var blocks = new List<TronBlock>();

        await foreach (var block in stream.StreamBlocksAsync(cts.Token))
        {
            blocks.Add(block);
            break; // just need 1 block
        }

        Assert.Single(blocks);
        Assert.Single(blocks[0].Transactions);
        Assert.Equal("txhash1", blocks[0].Transactions[0].TxId);
        Assert.Equal("TransferContract", blocks[0].Transactions[0].ContractType);
        Assert.Equal("41aabbccdd00112233445566778899aabbccddeeff", blocks[0].Transactions[0].FromAddress);
        Assert.Equal("41112233445566778899aabbccddeeff00112233aa", blocks[0].Transactions[0].ToAddress);
    }

    [Fact]
    public async Task StreamBlocksAsync_FetchesFullBlockWhenNowBlockHasNoTxData()
    {
        var provider = Substitute.For<ITronProvider>();
        // GetNowBlockAsync returns a block with TransactionCount>0 but no Transactions list
        provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(10, "b10", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 2, Array.Empty<byte>()));

        var fullBlockTxs = new List<BlockTransactionInfo>
        {
            new("txA", "TransferContract", "41aaaa", "41bbbb", 1_000_000, null, null),
            new("txB", "TriggerSmartContract", "41cccc", "41dddd", 0, "41eeee", new byte[] { 0xa9, 0x05, 0x9c, 0xbb }),
        };
        provider.GetBlockByNumAsync(10, Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(10, "b10", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 2, Array.Empty<byte>(), fullBlockTxs));

        var stream = new PollingBlockStream(provider, intervalMs: 50);
        var cts = new CancellationTokenSource(300);
        var blocks = new List<TronBlock>();

        await foreach (var block in stream.StreamBlocksAsync(cts.Token))
        {
            blocks.Add(block);
            break;
        }

        Assert.Single(blocks);
        Assert.Equal(2, blocks[0].Transactions.Count);
        Assert.Equal("txA", blocks[0].Transactions[0].TxId);
        Assert.Equal("txB", blocks[0].Transactions[1].TxId);
        // Verify GetBlockByNumAsync was called
        await provider.Received(1).GetBlockByNumAsync(10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StreamBlocksAsync_BackfillsSkippedBlocksWhenHeadJumps()
    {
        // Head advances from 100 -> 103 between polls. The intermediate blocks
        // 101 and 102 must not be dropped — they are fetched via GetBlockByNumAsync.
        var provider = Substitute.For<ITronProvider>();
        var nowBlockCall = 0;
        provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                nowBlockCall++;
                return nowBlockCall == 1
                    ? new BlockInfo(100, "b100", 0, 0, Array.Empty<byte>())
                    : new BlockInfo(103, "b103", 0, 0, Array.Empty<byte>());
            });
        provider.GetBlockByNumAsync(101, Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(101, "b101", 0, 0, Array.Empty<byte>()));
        provider.GetBlockByNumAsync(102, Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(102, "b102", 0, 0, Array.Empty<byte>()));

        var stream = new PollingBlockStream(provider, intervalMs: 50);
        var cts = new CancellationTokenSource(500);
        var blocks = new List<TronBlock>();

        await foreach (var block in stream.StreamBlocksAsync(cts.Token))
        {
            blocks.Add(block);
            if (blocks.Count >= 4) break;
        }

        Assert.Equal(4, blocks.Count);
        Assert.Equal(100, blocks[0].BlockNumber);
        Assert.Equal(101, blocks[1].BlockNumber);
        Assert.Equal(102, blocks[2].BlockNumber);
        Assert.Equal(103, blocks[3].BlockNumber);
    }

    [Fact]
    public async Task StreamBlocksAsync_BackfillPreservesTransactionsForMiddleBlocks()
    {
        // Head jumps 200 -> 202; middle block 201 carries the txs we must not lose.
        var provider = Substitute.For<ITronProvider>();
        var nowBlockCall = 0;
        provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                nowBlockCall++;
                return nowBlockCall == 1
                    ? new BlockInfo(200, "b200", 0, 0, Array.Empty<byte>())
                    : new BlockInfo(202, "b202", 0, 0, Array.Empty<byte>());
            });
        var middleTxs = new List<BlockTransactionInfo>
        {
            new("tx-in-201", "TransferContract", "41aaaa", "41bbbb", 7_000_000, null, null),
        };
        provider.GetBlockByNumAsync(201, Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(201, "b201", 0, 1, Array.Empty<byte>(), middleTxs));

        var stream = new PollingBlockStream(provider, intervalMs: 50);
        var cts = new CancellationTokenSource(500);
        var blocks = new List<TronBlock>();

        await foreach (var block in stream.StreamBlocksAsync(cts.Token))
        {
            blocks.Add(block);
            if (blocks.Count >= 3) break;
        }

        Assert.Equal(3, blocks.Count);
        var block201 = blocks.Single(b => b.BlockNumber == 201);
        Assert.Single(block201.Transactions);
        Assert.Equal("tx-in-201", block201.Transactions[0].TxId);
    }

    [Fact]
    public async Task StreamBlocksAsync_MidBackfillFailureDoesNotDropOrDuplicateBlocks()
    {
        // Head jumps 100 -> 103. Fetching block 102 throws on the FIRST attempt.
        // Expected: first poll yields 100, second poll yields 101 then fails at 102
        // (lastBlockNumber parks at 101, not rolled back), third poll retries 102
        // successfully and continues to 103. Final sequence: 100, 101, 102, 103 — no gap,
        // no duplicate (i.e. 101 must not appear twice).
        var provider = Substitute.For<ITronProvider>();
        var nowBlockCall = 0;
        provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                nowBlockCall++;
                return nowBlockCall == 1
                    ? new BlockInfo(100, "b100", 0, 0, Array.Empty<byte>())
                    : new BlockInfo(103, "b103", 0, 0, Array.Empty<byte>());
            });
        provider.GetBlockByNumAsync(101, Arg.Any<CancellationToken>())
            .Returns(new BlockInfo(101, "b101", 0, 0, Array.Empty<byte>()));
        var call102 = 0;
        provider.GetBlockByNumAsync(102, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                call102++;
                if (call102 == 1) throw new HttpRequestException("transient");
                return new BlockInfo(102, "b102", 0, 0, Array.Empty<byte>());
            });

        var stream = new PollingBlockStream(provider, intervalMs: 50);
        var cts = new CancellationTokenSource(1000);
        var blocks = new List<TronBlock>();

        await foreach (var block in stream.StreamBlocksAsync(cts.Token))
        {
            blocks.Add(block);
            if (blocks.Count >= 4) break;
        }

        Assert.Equal(4, blocks.Count);
        Assert.Equal(new long[] { 100, 101, 102, 103 },
            blocks.Select(b => b.BlockNumber).ToArray());
    }

    [Fact]
    public async Task StreamBlocksAsync_CapsBackfillAtMaxBlocksPerPoll()
    {
        // Head jumps 100 -> 105 (gap of 5). With maxBlocksPerPoll=2, each poll yields
        // at most 2 blocks. First poll seeds and yields 100. Subsequent polls yield
        // 101/102, then 103/104, then 105 — still in order, still no drops, but
        // bounded per-poll so cancellation stays responsive and RPC load is paced.
        var provider = Substitute.For<ITronProvider>();
        var nowBlockCall = 0;
        provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                nowBlockCall++;
                return nowBlockCall == 1
                    ? new BlockInfo(100, "b100", 0, 0, Array.Empty<byte>())
                    : new BlockInfo(105, "b105", 0, 0, Array.Empty<byte>());
            });
        for (long n = 101; n <= 105; n++)
        {
            var blockNum = n;
            provider.GetBlockByNumAsync(blockNum, Arg.Any<CancellationToken>())
                .Returns(new BlockInfo(blockNum, $"b{blockNum}", 0, 0, Array.Empty<byte>()));
        }

        var stream = new PollingBlockStream(provider, intervalMs: 30, maxBlocksPerPoll: 2);
        var cts = new CancellationTokenSource(1000);
        var blocks = new List<TronBlock>();

        await foreach (var block in stream.StreamBlocksAsync(cts.Token))
        {
            blocks.Add(block);
            if (blocks.Count >= 6) break;
        }

        Assert.Equal(6, blocks.Count);
        Assert.Equal(new long[] { 100, 101, 102, 103, 104, 105 },
            blocks.Select(b => b.BlockNumber).ToArray());
    }

    [Fact]
    public void Constructor_RejectsNonPositiveMaxBlocksPerPoll()
    {
        var provider = Substitute.For<ITronProvider>();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PollingBlockStream(provider, maxBlocksPerPoll: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PollingBlockStream(provider, maxBlocksPerPoll: -1));
    }

    [Fact]
    public void ConvertTransactions_NullReturnsEmpty()
    {
        var result = PollingBlockStream.ConvertTransactions(null);
        Assert.Empty(result);
    }

    [Fact]
    public void ConvertTransactions_PopulatesFromAndToAddresses()
    {
        var txInfos = new List<BlockTransactionInfo>
        {
            new("tx1", "TransferContract", "41aabb", "41ccdd", 1_000_000, null, null),
        };
        var result = PollingBlockStream.ConvertTransactions(txInfos);
        Assert.Single(result);
        Assert.Equal("41aabb", result[0].FromAddress);
        Assert.Equal("41ccdd", result[0].ToAddress);
        Assert.Equal("TransferContract", result[0].ContractType);
    }

    [Fact]
    public void BuildRawData_EncodesAmountAndContractInfo()
    {
        var tx = new BlockTransactionInfo(
            "tx1", "TriggerSmartContract", "41owner", "41contract",
            500_000, "41contractaddr", new byte[] { 0x01, 0x02 });

        var raw = PollingBlockStream.BuildRawData(tx);
        Assert.NotNull(raw);
        Assert.True(raw.Length > 16); // 8 + 4 + contractAddr + 4 + data
    }
}
