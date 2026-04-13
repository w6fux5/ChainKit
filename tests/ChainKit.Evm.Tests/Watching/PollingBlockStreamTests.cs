using System.Numerics;
using System.Text.Json;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ChainKit.Evm.Models;
using ChainKit.Evm.Providers;
using ChainKit.Evm.Watching;
using Xunit;

namespace ChainKit.Evm.Tests.Watching;

public class PollingBlockStreamTests
{
    private static IEvmProvider MockProvider() => Substitute.For<IEvmProvider>();

    /// <summary>
    /// Builds a JSON block element that mimics a real Ethereum node response.
    /// </summary>
    private static JsonElement MakeBlockJson(long blockNumber, params (string txHash, string from, string to, string value)[] txs)
    {
        var hexNum = "0x" + blockNumber.ToString("x");
        var timestamp = "0x" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString("x");
        var txArray = string.Join(",", txs.Select(t =>
            $$"""{"hash":"{{t.txHash}}","from":"{{t.from}}","to":"{{t.to}}","value":"{{t.value}}","input":"0x"}"""));
        var json = $$"""{"number":"{{hexNum}}","hash":"0xblockhash{{blockNumber}}","timestamp":"{{timestamp}}","transactions":[{{txArray}}]}""";
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PollingBlockStream(null!));
    }

    [Fact]
    public async Task GetBlocksAsync_YieldsBlocksInOrder()
    {
        var provider = MockProvider();
        var blockNum = 100L;
        provider.GetBlockByNumberAsync(Arg.Any<long>(), true, Arg.Any<CancellationToken>())
            .Returns(args =>
            {
                var num = (long)args[0];
                if (num > blockNum + 2) return Task.FromResult<JsonElement?>(null);
                return Task.FromResult<JsonElement?>(MakeBlockJson(num));
            });

        var stream = new PollingBlockStream(provider, pollInterval: TimeSpan.FromMilliseconds(50));
        var cts = new CancellationTokenSource(500);
        var blocks = new List<EvmBlock>();

        await foreach (var block in stream.GetBlocksAsync(blockNum, cts.Token))
            blocks.Add(block);

        Assert.True(blocks.Count >= 2);
        Assert.Equal(100, blocks[0].BlockNumber);
        Assert.Equal(101, blocks[1].BlockNumber);
    }

    [Fact]
    public async Task GetBlocksAsync_NullBlockCausesWait()
    {
        var provider = MockProvider();
        var callCount = 0;
        provider.GetBlockByNumberAsync(Arg.Any<long>(), true, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref callCount);
                // First call returns null (caught up), subsequent calls also null
                return Task.FromResult<JsonElement?>(null);
            });

        var stream = new PollingBlockStream(provider, pollInterval: TimeSpan.FromMilliseconds(50));
        var cts = new CancellationTokenSource(300);
        var blocks = new List<EvmBlock>();

        await foreach (var block in stream.GetBlocksAsync(1, cts.Token))
            blocks.Add(block);

        Assert.Empty(blocks); // no blocks yielded because all were null
        Assert.True(callCount >= 2); // but polling retried multiple times
    }

    [Fact]
    public async Task GetBlocksAsync_NullThenBlock_YieldsAfterWait()
    {
        var provider = MockProvider();
        var callCount = 0;
        provider.GetBlockByNumberAsync(Arg.Any<long>(), true, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var c = Interlocked.Increment(ref callCount);
                if (c <= 2) return Task.FromResult<JsonElement?>(null);
                return Task.FromResult<JsonElement?>(MakeBlockJson(1));
            });

        var stream = new PollingBlockStream(provider, pollInterval: TimeSpan.FromMilliseconds(50));
        var cts = new CancellationTokenSource(500);
        var blocks = new List<EvmBlock>();

        await foreach (var block in stream.GetBlocksAsync(1, cts.Token))
        {
            blocks.Add(block);
            break; // just need the first block
        }

        Assert.Single(blocks);
        Assert.Equal(1, blocks[0].BlockNumber);
        Assert.True(callCount >= 3);
    }

    [Fact]
    public async Task GetBlocksAsync_HandlesProviderErrors()
    {
        var provider = MockProvider();
        var callCount = 0;
        provider.GetBlockByNumberAsync(Arg.Any<long>(), true, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var c = Interlocked.Increment(ref callCount);
                if (c == 1) throw new HttpRequestException("timeout");
                return Task.FromResult<JsonElement?>(MakeBlockJson(1));
            });

        var stream = new PollingBlockStream(provider, pollInterval: TimeSpan.FromMilliseconds(50));
        var cts = new CancellationTokenSource(500);
        var blocks = new List<EvmBlock>();

        await foreach (var block in stream.GetBlocksAsync(1, cts.Token))
        {
            blocks.Add(block);
            break;
        }

        Assert.Single(blocks); // recovered after error
        Assert.True(callCount >= 2);
    }

    [Fact]
    public async Task GetBlocksAsync_RespectsCancellation()
    {
        var provider = MockProvider();
        provider.GetBlockByNumberAsync(Arg.Any<long>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JsonElement?>(MakeBlockJson(1)));

        var stream = new PollingBlockStream(provider, pollInterval: TimeSpan.FromSeconds(10));
        var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel immediately

        var blocks = new List<EvmBlock>();
        await foreach (var block in stream.GetBlocksAsync(1, cts.Token))
            blocks.Add(block);

        // Should complete quickly without hanging
    }

    [Fact]
    public async Task GetBlocksAsync_IncludesTransactions()
    {
        var provider = MockProvider();
        var blockJson = MakeBlockJson(42,
            ("0xabc", "0xfrom1", "0xto1", "0xde0b6b3a7640000"), // 1 ETH
            ("0xdef", "0xfrom2", "0xto2", "0x0"));

        provider.GetBlockByNumberAsync(42, true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JsonElement?>(blockJson));
        provider.GetBlockByNumberAsync(Arg.Is<long>(n => n != 42), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JsonElement?>(null));

        var stream = new PollingBlockStream(provider, pollInterval: TimeSpan.FromMilliseconds(50));
        var cts = new CancellationTokenSource(500);
        var blocks = new List<EvmBlock>();

        await foreach (var block in stream.GetBlocksAsync(42, cts.Token))
        {
            blocks.Add(block);
            break;
        }

        Assert.Single(blocks);
        Assert.Equal(42, blocks[0].BlockNumber);
        Assert.Equal(2, blocks[0].Transactions.Count);

        var tx0 = blocks[0].Transactions[0];
        Assert.Equal("0xabc", tx0.TxHash);
        Assert.Equal("0xfrom1", tx0.From);
        Assert.Equal("0xto1", tx0.To);
        Assert.True(tx0.Value > 0);

        var tx1 = blocks[0].Transactions[1];
        Assert.Equal("0xdef", tx1.TxHash);
        Assert.Equal(BigInteger.Zero, tx1.Value);
    }

    [Fact]
    public void ParseBlock_NullToField_ParsesAsEmpty()
    {
        // Contract creation transactions have null "to"
        var json = """{"number":"0xa","hash":"0xhash","timestamp":"0x60000000","transactions":[{"hash":"0xtx1","from":"0xsender","to":null,"value":"0x0","input":"0x"}]}""";
        using var doc = JsonDocument.Parse(json);
        var block = PollingBlockStream.ParseBlock(doc.RootElement.Clone(), 10);

        Assert.Single(block.Transactions);
        Assert.Equal("", block.Transactions[0].To);
    }

    [Fact]
    public void ParseBlock_StringOnlyTransactions_AreSkipped()
    {
        // When fullTx=false, transactions are just hashes (strings)
        var json = """{"number":"0xa","hash":"0xhash","timestamp":"0x60000000","transactions":["0xhash1","0xhash2"]}""";
        using var doc = JsonDocument.Parse(json);
        var block = PollingBlockStream.ParseBlock(doc.RootElement.Clone(), 10);

        Assert.Empty(block.Transactions);
        Assert.Equal(10, block.BlockNumber);
    }

    [Fact]
    public void ParseBlock_EmptyTransactions_ReturnsEmptyList()
    {
        var json = """{"number":"0x1","hash":"0xhash","timestamp":"0x60000000","transactions":[]}""";
        using var doc = JsonDocument.Parse(json);
        var block = PollingBlockStream.ParseBlock(doc.RootElement.Clone(), 1);

        Assert.Empty(block.Transactions);
    }

    [Fact]
    public void ParseBlock_InputWithData_ParsesHexCorrectly()
    {
        // transfer(address,uint256) function call
        var inputHex = "0xa9059cbb0000000000000000000000001234567890abcdef1234567890abcdef123456780000000000000000000000000000000000000000000000000000000000989680";
        var json = $$"""{"number":"0x5","hash":"0xhash","timestamp":"0x60000000","transactions":[{"hash":"0xtx1","from":"0xsender","to":"0xcontract","value":"0x0","input":"{{inputHex}}"}]}""";
        using var doc = JsonDocument.Parse(json);
        var block = PollingBlockStream.ParseBlock(doc.RootElement.Clone(), 5);

        Assert.Single(block.Transactions);
        Assert.True(block.Transactions[0].Input.Length > 0);
        // First 4 bytes should be the function selector a9059cbb
        Assert.Equal(0xa9, block.Transactions[0].Input[0]);
        Assert.Equal(0x05, block.Transactions[0].Input[1]);
        Assert.Equal(0x9c, block.Transactions[0].Input[2]);
        Assert.Equal(0xbb, block.Transactions[0].Input[3]);
    }

    [Fact]
    public void ParseBlock_EmptyInput_ReturnsEmptyArray()
    {
        var json = """{"number":"0x1","hash":"0xhash","timestamp":"0x60000000","transactions":[{"hash":"0xtx1","from":"0xsender","to":"0xrecv","value":"0x0","input":"0x"}]}""";
        using var doc = JsonDocument.Parse(json);
        var block = PollingBlockStream.ParseBlock(doc.RootElement.Clone(), 1);

        Assert.Single(block.Transactions);
        Assert.Empty(block.Transactions[0].Input);
    }

    [Fact]
    public async Task DisposeAsync_CompletesSuccessfully()
    {
        var stream = new PollingBlockStream(MockProvider());
        await stream.DisposeAsync();
        // Should not throw
    }
}
