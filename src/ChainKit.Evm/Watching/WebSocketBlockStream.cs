using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ChainKit.Evm.Models;
using ChainKit.Evm.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChainKit.Evm.Watching;

/// <summary>
/// Subscribes to <c>eth_subscribe("newHeads")</c> via WebSocket for real-time block notifications.
/// On each new head, fetches the full block via <see cref="IEvmProvider.GetBlockByNumberAsync"/>.
/// Features auto-reconnect with exponential backoff and gap detection on reconnect.
/// </summary>
public sealed class WebSocketBlockStream : IEvmBlockStream
{
    private readonly string _wsUrl;
    private readonly IEvmProvider _provider;
    private readonly ILogger<WebSocketBlockStream> _logger;
    private readonly TimeSpan _initialBackoff;
    private readonly TimeSpan _maxBackoff;

    /// <summary>
    /// Creates a new WebSocketBlockStream instance.
    /// </summary>
    /// <param name="wsUrl">The WebSocket endpoint URL (e.g. wss://eth-mainnet.g.alchemy.com/v2/KEY).</param>
    /// <param name="provider">The EVM provider for fetching full blocks.</param>
    /// <param name="initialBackoff">Initial reconnect backoff delay. Defaults to 1 second.</param>
    /// <param name="maxBackoff">Maximum reconnect backoff delay. Defaults to 30 seconds.</param>
    /// <param name="logger">Optional logger. Defaults to NullLogger.</param>
    public WebSocketBlockStream(string wsUrl, IEvmProvider provider,
        TimeSpan? initialBackoff = null, TimeSpan? maxBackoff = null,
        ILogger<WebSocketBlockStream>? logger = null)
    {
        _wsUrl = wsUrl ?? throw new ArgumentNullException(nameof(wsUrl));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _initialBackoff = initialBackoff ?? TimeSpan.FromSeconds(1);
        _maxBackoff = maxBackoff ?? TimeSpan.FromSeconds(30);
        _logger = logger ?? NullLogger<WebSocketBlockStream>.Instance;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<EvmBlock> GetBlocksAsync(long startBlock,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = Channel.CreateBounded<EvmBlock>(new BoundedChannelOptions(32)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        // Start the WebSocket producer loop in a background task
        var producerTask = Task.Run(() => ProduceBlocksAsync(channel.Writer, startBlock, ct), ct);

        await foreach (var block in channel.Reader.ReadAllAsync(ct))
        {
            yield return block;
        }

        // Await the producer to propagate any unhandled exceptions
        await producerTask;
    }

    /// <summary>
    /// Background producer that connects to WebSocket, subscribes to newHeads,
    /// fetches full blocks, and writes them to the channel. Reconnects on failure.
    /// </summary>
    private async Task ProduceBlocksAsync(ChannelWriter<EvmBlock> writer, long startBlock, CancellationToken ct)
    {
        long lastYieldedBlock = startBlock - 1;
        var backoff = _initialBackoff;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ClientWebSocket? ws = null;
                try
                {
                    ws = new ClientWebSocket();
                    await ws.ConnectAsync(new Uri(_wsUrl), ct);
                    _logger.LogInformation("WebSocket connected to {Url}", _wsUrl);
                    backoff = _initialBackoff; // reset backoff on successful connection

                    // Send eth_subscribe for newHeads
                    var subscribeMsg = """{"jsonrpc":"2.0","id":1,"method":"eth_subscribe","params":["newHeads"]}""";
                    var sendBuf = Encoding.UTF8.GetBytes(subscribeMsg);
                    await ws.SendAsync(sendBuf, WebSocketMessageType.Text, true, ct);

                    // Read subscription confirmation
                    var confirmJson = await ReceiveFullMessageAsync(ws, ct);
                    if (confirmJson == null)
                    {
                        _logger.LogWarning("WebSocket closed before subscription confirmation");
                        continue;
                    }
                    _logger.LogDebug("Subscription confirmed: {Response}", confirmJson);

                    // Fill any gaps since last yielded block
                    var currentTip = await _provider.GetBlockNumberAsync(ct);
                    for (var gap = lastYieldedBlock + 1; gap <= currentTip; gap++)
                    {
                        if (ct.IsCancellationRequested) return;
                        var gapBlock = await FetchBlockAsync(gap, ct);
                        if (gapBlock != null)
                        {
                            lastYieldedBlock = gapBlock.BlockNumber;
                            await writer.WriteAsync(gapBlock, ct);
                        }
                    }

                    // Listen for newHeads notifications
                    while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                    {
                        var message = await ReceiveFullMessageAsync(ws, ct);
                        if (message == null) break; // connection closed

                        var blockNumber = ExtractBlockNumberFromNotification(message);
                        if (blockNumber == null) continue;

                        // Fill gaps (e.g. if we missed blocks)
                        for (var num = lastYieldedBlock + 1; num <= blockNumber.Value; num++)
                        {
                            if (ct.IsCancellationRequested) return;
                            var block = await FetchBlockAsync(num, ct);
                            if (block != null)
                            {
                                lastYieldedBlock = block.BlockNumber;
                                await writer.WriteAsync(block, ct);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "WebSocket error, reconnecting in {Backoff}...", backoff);
                }
                finally
                {
                    if (ws != null)
                    {
                        try
                        {
                            if (ws.State == WebSocketState.Open)
                                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        }
                        catch { /* best-effort close */ }
                        ws.Dispose();
                    }
                }

                if (ct.IsCancellationRequested) return;

                // Exponential backoff before reconnect
                try { await Task.Delay(backoff, ct); }
                catch (OperationCanceledException) { return; }

                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, _maxBackoff.Ticks));
            }
        }
        finally
        {
            writer.Complete();
        }
    }

    /// <summary>
    /// Fetches and parses a full block by number. Returns null if the block doesn't exist.
    /// </summary>
    private async Task<EvmBlock?> FetchBlockAsync(long blockNumber, CancellationToken ct)
    {
        try
        {
            var blockData = await _provider.GetBlockByNumberAsync(blockNumber, true, ct);
            if (blockData == null) return null;
            return PollingBlockStream.ParseBlock(blockData.Value, blockNumber);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch block {BlockNumber}", blockNumber);
            return null;
        }
    }

    /// <summary>
    /// Extracts the block number from an eth_subscription newHeads notification.
    /// Expected format: {"jsonrpc":"2.0","method":"eth_subscription","params":{"result":{"number":"0x..."}}}
    /// </summary>
    internal static long? ExtractBlockNumberFromNotification(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            if (doc.RootElement.TryGetProperty("params", out var paramsEl)
                && paramsEl.TryGetProperty("result", out var resultEl)
                && resultEl.TryGetProperty("number", out var numEl)
                && numEl.GetString() is string numStr
                && numStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToInt64(numStr[2..], 16);
            }
        }
        catch { /* not a valid notification */ }
        return null;
    }

    /// <summary>
    /// Receives a complete WebSocket text message, handling fragmentation.
    /// Returns null if the connection is closed.
    /// </summary>
    private static async Task<string?> ReceiveFullMessageAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            return Encoding.UTF8.GetString(ms.ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
