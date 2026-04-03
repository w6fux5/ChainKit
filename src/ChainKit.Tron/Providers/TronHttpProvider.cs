using System.Text.Json;
using System.Text.Json.Serialization;
using ChainKit.Tron.Crypto;
using ChainKit.Tron.Models;
using ChainKit.Tron.Protocol.Protobuf;
using Google.Protobuf;

namespace ChainKit.Tron.Providers;

public class TronHttpProvider : ITronProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public TronHttpProvider(string baseUrl, string? apiKey = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient();
        if (apiKey != null)
            _httpClient.DefaultRequestHeaders.Add("TRON-PRO-API-KEY", apiKey);
    }

    public TronHttpProvider(TronNetworkConfig network, string? apiKey = null)
        : this(network.HttpEndpoint, apiKey) { }

    internal TronHttpProvider(HttpClient httpClient, string baseUrl)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    // --- ITronProvider implementation ---

    public async Task<AccountInfo> GetAccountAsync(string address, CancellationToken ct = default)
    {
        var hexAddress = NormalizeToHex(address);
        var json = await PostAsync("/wallet/getaccount",
            new { address = hexAddress, visible = false }, ct);

        var root = JsonDocument.Parse(json).RootElement;

        var addr = root.TryGetProperty("address", out var addrEl) ? addrEl.GetString() ?? "" : hexAddress;
        var balance = root.TryGetProperty("balance", out var balEl) ? balEl.GetInt64() : 0;
        var netUsage = root.TryGetProperty("net_usage", out var netEl) ? netEl.GetInt64() : 0;
        var energyUsage = root.TryGetProperty("account_resource", out var arEl)
            && arEl.TryGetProperty("energy_usage", out var euEl) ? euEl.GetInt64() : 0;
        var createTime = root.TryGetProperty("create_time", out var ctEl) ? ctEl.GetInt64() : 0;

        return new AccountInfo(addr, balance, netUsage, energyUsage, createTime);
    }

    public async Task<BlockInfo> GetNowBlockAsync(CancellationToken ct = default)
    {
        var json = await PostAsync("/wallet/getnowblock", new { }, ct);
        return ParseBlockInfo(json);
    }

    public async Task<BlockInfo> GetBlockByNumAsync(long num, CancellationToken ct = default)
    {
        var json = await PostAsync("/wallet/getblockbynum", new { num }, ct);
        return ParseBlockInfo(json);
    }

    public async Task<Transaction> CreateTransactionAsync(Transaction transaction, CancellationToken ct = default)
    {
        // TronGrid's createtransaction endpoint accepts protobuf-JSON format
        var txJson = JsonFormatter.Default.Format(transaction);
        var response = await PostRawAsync("/wallet/createtransaction", txJson, ct);
        var parser = new JsonParser(JsonParser.Settings.Default);
        return parser.Parse<Transaction>(response);
    }

    public async Task<BroadcastResult> BroadcastTransactionAsync(Transaction signedTx, CancellationToken ct = default)
    {
        var txJson = JsonFormatter.Default.Format(signedTx);
        var json = await PostRawAsync("/wallet/broadcasttransaction", txJson, ct);

        var root = JsonDocument.Parse(json).RootElement;
        var success = root.TryGetProperty("result", out var resEl) && resEl.GetBoolean();
        var txId = root.TryGetProperty("txid", out var txIdEl) ? txIdEl.GetString() : null;
        var message = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : null;

        // TronGrid returns hex-encoded error messages
        if (message != null && IsHexString(message))
            message = DecodeHexMessage(message);

        return new BroadcastResult(success, txId, message);
    }

    public async Task<TransactionInfoDto> GetTransactionByIdAsync(string txId, CancellationToken ct = default)
    {
        var json = await PostAsync("/wallet/gettransactionbyid", new { value = txId }, ct);
        return ParseTransactionInfoFromTx(json, txId);
    }

    public async Task<TransactionInfoDto> GetTransactionInfoByIdAsync(string txId, CancellationToken ct = default)
    {
        var json = await PostAsync("/walletsolidity/gettransactioninfobyid", new { value = txId }, ct);
        return ParseTransactionInfo(json, txId);
    }

    public async Task<Transaction> TriggerSmartContractAsync(
        string ownerAddress, string contractAddress,
        string functionSelector, byte[] parameter,
        long feeLimit, long callValue = 0,
        CancellationToken ct = default)
    {
        var hexOwner = NormalizeToHex(ownerAddress);
        var hexContract = NormalizeToHex(contractAddress);
        var paramHex = Convert.ToHexString(parameter).ToLowerInvariant();

        var body = new
        {
            owner_address = hexOwner,
            contract_address = hexContract,
            function_selector = functionSelector,
            parameter = paramHex,
            fee_limit = feeLimit,
            call_value = callValue,
            visible = false
        };

        var json = await PostAsync("/wallet/triggersmartcontract", body, ct);
        var root = JsonDocument.Parse(json).RootElement;

        if (root.TryGetProperty("transaction", out var txEl))
        {
            var parser = new JsonParser(JsonParser.Settings.Default);
            return parser.Parse<Transaction>(txEl.GetRawText());
        }

        var errorMsg = root.TryGetProperty("result", out var resEl)
            && resEl.TryGetProperty("message", out var msgEl)
                ? msgEl.GetString() : "Unknown error from triggersmartcontract";
        throw new InvalidOperationException(errorMsg);
    }

    public async Task<byte[]> TriggerConstantContractAsync(
        string ownerAddress, string contractAddress,
        string functionSelector, byte[] parameter,
        CancellationToken ct = default)
    {
        var hexOwner = NormalizeToHex(ownerAddress);
        var hexContract = NormalizeToHex(contractAddress);
        var paramHex = Convert.ToHexString(parameter).ToLowerInvariant();

        var body = new
        {
            owner_address = hexOwner,
            contract_address = hexContract,
            function_selector = functionSelector,
            parameter = paramHex,
            visible = false
        };

        var json = await PostAsync("/wallet/triggerconstantcontract", body, ct);
        var root = JsonDocument.Parse(json).RootElement;

        if (root.TryGetProperty("constant_result", out var resultArray)
            && resultArray.GetArrayLength() > 0)
        {
            var hexResult = resultArray[0].GetString() ?? "";
            return Convert.FromHexString(hexResult);
        }

        var errorMsg = root.TryGetProperty("result", out var resEl)
            && resEl.TryGetProperty("message", out var msgEl)
                ? msgEl.GetString() : "No result from triggerconstantcontract";

        if (errorMsg != null && IsHexString(errorMsg))
            errorMsg = DecodeHexMessage(errorMsg);

        throw new InvalidOperationException(errorMsg);
    }

    public async Task<AccountResourceInfo> GetAccountResourceAsync(string address, CancellationToken ct = default)
    {
        var hexAddress = NormalizeToHex(address);
        var json = await PostAsync("/wallet/getaccountresource",
            new { address = hexAddress, visible = false }, ct);

        var root = JsonDocument.Parse(json).RootElement;

        long GetLong(string name) =>
            root.TryGetProperty(name, out var el) ? el.GetInt64() : 0;

        return new AccountResourceInfo(
            FreeBandwidthLimit: GetLong("freeNetLimit"),
            FreeBandwidthUsed: GetLong("freeNetUsed"),
            EnergyLimit: GetLong("EnergyLimit"),
            EnergyUsed: GetLong("EnergyUsed"),
            TotalBandwidthLimit: GetLong("NetLimit"),
            TotalBandwidthUsed: GetLong("NetUsed"));
    }

    public async Task<long> EstimateEnergyAsync(
        string ownerAddress, string contractAddress,
        string functionSelector, byte[] parameter,
        CancellationToken ct = default)
    {
        var hexOwner = NormalizeToHex(ownerAddress);
        var hexContract = NormalizeToHex(contractAddress);
        var paramHex = Convert.ToHexString(parameter).ToLowerInvariant();

        var body = new
        {
            owner_address = hexOwner,
            contract_address = hexContract,
            function_selector = functionSelector,
            parameter = paramHex,
            visible = false
        };

        var json = await PostAsync("/wallet/estimateenergy", body, ct);
        var root = JsonDocument.Parse(json).RootElement;

        if (root.TryGetProperty("energy_required", out var energyEl))
            return energyEl.GetInt64();

        return 0;
    }

    // --- Private helpers ---

    private async Task<string> PostAsync(string path, object body, CancellationToken ct)
    {
        var jsonBody = JsonSerializer.Serialize(body, JsonOptions);
        return await PostRawAsync(path, jsonBody, ct);
    }

    private async Task<string> PostRawAsync(string path, string jsonBody, CancellationToken ct)
    {
        var url = _baseUrl + path;
        using var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static string NormalizeToHex(string address)
    {
        // If it starts with T, it's base58 - convert to hex
        if (address.StartsWith('T'))
            return TronAddress.ToHex(address);
        return address;
    }

    private static BlockInfo ParseBlockInfo(string json)
    {
        var root = JsonDocument.Parse(json).RootElement;

        long blockNumber = 0;
        string blockId = "";
        long timestamp = 0;
        int txCount = 0;
        byte[] rawData = Array.Empty<byte>();

        if (root.TryGetProperty("block_header", out var headerEl)
            && headerEl.TryGetProperty("raw_data", out var rawEl))
        {
            blockNumber = rawEl.TryGetProperty("number", out var numEl) ? numEl.GetInt64() : 0;
            timestamp = rawEl.TryGetProperty("timestamp", out var tsEl) ? tsEl.GetInt64() : 0;
            rawData = System.Text.Encoding.UTF8.GetBytes(rawEl.GetRawText());
        }

        if (root.TryGetProperty("blockID", out var idEl))
            blockId = idEl.GetString() ?? "";

        if (root.TryGetProperty("transactions", out var txsEl))
            txCount = txsEl.GetArrayLength();

        return new BlockInfo(blockNumber, blockId, timestamp, txCount, rawData);
    }

    private static TransactionInfoDto ParseTransactionInfoFromTx(string json, string txId)
    {
        var root = JsonDocument.Parse(json).RootElement;

        var id = root.TryGetProperty("txID", out var idEl) ? idEl.GetString() ?? txId : txId;
        // gettransactionbyid doesn't return block/fee info; those come from gettransactioninfobyid
        var contractResult = "";
        if (root.TryGetProperty("ret", out var retEl) && retEl.GetArrayLength() > 0)
        {
            var first = retEl[0];
            contractResult = first.TryGetProperty("contractRet", out var crEl)
                ? crEl.GetString() ?? "" : "";
        }

        return new TransactionInfoDto(id, 0, 0, contractResult, 0, 0, 0);
    }

    private static TransactionInfoDto ParseTransactionInfo(string json, string txId)
    {
        var root = JsonDocument.Parse(json).RootElement;

        var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? txId : txId;
        var blockNum = root.TryGetProperty("blockNumber", out var bnEl) ? bnEl.GetInt64() : 0;
        var blockTs = root.TryGetProperty("blockTimeStamp", out var btsEl) ? btsEl.GetInt64() : 0;
        var fee = root.TryGetProperty("fee", out var feeEl) ? feeEl.GetInt64() : 0;
        var energy = root.TryGetProperty("receipt", out var rcEl)
            && rcEl.TryGetProperty("energy_usage_total", out var euEl) ? euEl.GetInt64() : 0;
        var net = root.TryGetProperty("receipt", out var rc2El)
            && rc2El.TryGetProperty("net_usage", out var nuEl) ? nuEl.GetInt64() : 0;
        var contractResult = root.TryGetProperty("contractResult", out var crEl)
            && crEl.GetArrayLength() > 0
                ? crEl[0].GetString() ?? "" : "";

        return new TransactionInfoDto(id, blockNum, blockTs, contractResult, fee, energy, net);
    }

    private static bool IsHexString(string s) =>
        s.Length > 0 && s.Length % 2 == 0 && s.All(c => char.IsAsciiHexDigit(c));

    private static string DecodeHexMessage(string hex)
    {
        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromHexString(hex));
        }
        catch
        {
            return hex;
        }
    }
}
