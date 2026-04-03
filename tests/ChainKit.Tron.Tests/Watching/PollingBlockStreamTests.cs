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
