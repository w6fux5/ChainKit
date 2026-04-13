using System.Numerics;
using System.Text.Json;
using ChainKit.Core.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChainKit.Evm.Providers;

/// <summary>
/// HTTP-based JSON-RPC 2.0 provider for EVM-compatible blockchains.
/// </summary>
public sealed class EvmHttpProvider : IEvmProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _rpcUrl;
    private readonly ILogger<EvmHttpProvider> _logger;
    private long _requestId;

    /// <summary>
    /// Creates a new provider with the given RPC endpoint URL.
    /// </summary>
    public EvmHttpProvider(string rpcUrl, ILogger<EvmHttpProvider>? logger = null)
    {
        _rpcUrl = rpcUrl ?? throw new ArgumentNullException(nameof(rpcUrl));
        _httpClient = new HttpClient();
        _logger = logger ?? NullLogger<EvmHttpProvider>.Instance;
    }

    /// <summary>
    /// Creates a new provider from a pre-configured network.
    /// </summary>
    public EvmHttpProvider(EvmNetworkConfig network, ILogger<EvmHttpProvider>? logger = null)
        : this(network.RpcUrl, logger) { }

    /// <summary>
    /// Core JSON-RPC 2.0 request helper. All public methods delegate to this.
    /// </summary>
    private async Task<JsonElement> RpcAsync(string method, object[]? parameters = null, CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _requestId);
        var request = new { jsonrpc = "2.0", method, @params = parameters ?? Array.Empty<object>(), id };
        var json = JsonSerializer.Serialize(request);
        _logger.LogDebug("RPC -> {Method} id={Id}", method, id);

        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(_rpcUrl, content, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (doc.RootElement.TryGetProperty("error", out var error))
        {
            var msg = error.TryGetProperty("message", out var msgEl) ? msgEl.GetString() ?? "RPC error" : "RPC error";
            throw new InvalidOperationException($"JSON-RPC error: {msg}");
        }

        return doc.RootElement.GetProperty("result").Clone();
    }

    private static BigInteger ParseHexBigInteger(string hex)
    {
        var clean = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        if (clean.Length == 0) return BigInteger.Zero;
        // Prepend "0" to ensure positive parsing for unsigned values
        return BigInteger.Parse("0" + clean, System.Globalization.NumberStyles.HexNumber);
    }

    private static long ParseHexLong(string hex)
    {
        var clean = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        return Convert.ToInt64(clean, 16);
    }

    private static string ToHexParam(long value) => "0x" + value.ToString("x");

    private static string ToHexParam(BigInteger value) => "0x" + value.ToString("x");

    /// <inheritdoc />
    public async Task<BigInteger> GetBalanceAsync(string address, CancellationToken ct = default)
    {
        var result = await RpcAsync("eth_getBalance", new object[] { address, "latest" }, ct);
        return ParseHexBigInteger(result.GetString()!);
    }

    /// <inheritdoc />
    public async Task<long> GetTransactionCountAsync(string address, CancellationToken ct = default)
    {
        var result = await RpcAsync("eth_getTransactionCount", new object[] { address, "latest" }, ct);
        return ParseHexLong(result.GetString()!);
    }

    /// <inheritdoc />
    public async Task<string> GetCodeAsync(string address, CancellationToken ct = default)
    {
        var result = await RpcAsync("eth_getCode", new object[] { address, "latest" }, ct);
        return result.GetString()!;
    }

    /// <inheritdoc />
    public async Task<JsonElement?> GetBlockByNumberAsync(long blockNumber, bool fullTx = false, CancellationToken ct = default)
    {
        var result = await RpcAsync("eth_getBlockByNumber", new object[] { ToHexParam(blockNumber), fullTx }, ct);
        return result.ValueKind == JsonValueKind.Null ? null : result;
    }

    /// <inheritdoc />
    public async Task<long> GetBlockNumberAsync(CancellationToken ct = default)
    {
        var result = await RpcAsync("eth_blockNumber", null, ct);
        return ParseHexLong(result.GetString()!);
    }

    /// <inheritdoc />
    public async Task<string> SendRawTransactionAsync(byte[] signedTx, CancellationToken ct = default)
    {
        var result = await RpcAsync("eth_sendRawTransaction", new object[] { "0x" + signedTx.ToHex() }, ct);
        return result.GetString()!;
    }

    /// <inheritdoc />
    public async Task<JsonElement?> GetTransactionByHashAsync(string txHash, CancellationToken ct = default)
    {
        var result = await RpcAsync("eth_getTransactionByHash", new object[] { txHash }, ct);
        return result.ValueKind == JsonValueKind.Null ? null : result;
    }

    /// <inheritdoc />
    public async Task<JsonElement?> GetTransactionReceiptAsync(string txHash, CancellationToken ct = default)
    {
        var result = await RpcAsync("eth_getTransactionReceipt", new object[] { txHash }, ct);
        return result.ValueKind == JsonValueKind.Null ? null : result;
    }

    /// <inheritdoc />
    public async Task<string> CallAsync(string to, byte[] data, CancellationToken ct = default)
    {
        var callObject = new { to, data = "0x" + data.ToHex() };
        var result = await RpcAsync("eth_call", new object[] { callObject, "latest" }, ct);
        return result.GetString()!;
    }

    /// <inheritdoc />
    public async Task<long> EstimateGasAsync(string from, string to, byte[] data, BigInteger? value = null, CancellationToken ct = default)
    {
        var txObj = new Dictionary<string, string> { ["from"] = from, ["to"] = to, ["data"] = "0x" + data.ToHex() };
        if (value.HasValue && value.Value > 0) txObj["value"] = ToHexParam(value.Value);
        var result = await RpcAsync("eth_estimateGas", new object[] { txObj }, ct);
        return ParseHexLong(result.GetString()!);
    }

    /// <inheritdoc />
    public async Task<BigInteger> GetGasPriceAsync(CancellationToken ct = default)
    {
        var result = await RpcAsync("eth_gasPrice", null, ct);
        return ParseHexBigInteger(result.GetString()!);
    }

    /// <inheritdoc />
    public async Task<(BigInteger baseFee, BigInteger priorityFee)> GetEip1559FeesAsync(CancellationToken ct = default)
    {
        var blockResult = await RpcAsync("eth_getBlockByNumber", new object[] { "latest", false }, ct);
        var baseFee = ParseHexBigInteger(blockResult.GetProperty("baseFeePerGas").GetString()!);

        var priorityResult = await RpcAsync("eth_maxPriorityFeePerGas", null, ct);
        var priorityFee = ParseHexBigInteger(priorityResult.GetString()!);

        return (baseFee, priorityFee);
    }

    /// <inheritdoc />
    public async Task<JsonElement[]> GetLogsAsync(long fromBlock, long toBlock, string? address = null, string[]? topics = null, CancellationToken ct = default)
    {
        var filter = new Dictionary<string, object>
        {
            ["fromBlock"] = ToHexParam(fromBlock),
            ["toBlock"] = ToHexParam(toBlock)
        };
        if (address != null) filter["address"] = address;
        if (topics != null) filter["topics"] = topics;

        var result = await RpcAsync("eth_getLogs", new object[] { filter }, ct);
        return result.EnumerateArray().Select(e => e.Clone()).ToArray();
    }

    /// <summary>
    /// Disposes the internal HttpClient.
    /// </summary>
    public void Dispose() => _httpClient.Dispose();
}
