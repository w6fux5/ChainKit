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

        // Parse frozenV2 array for Stake 2.0 staked amounts.
        // Each element has optional "type" (string: "BANDWIDTH" or "ENERGY", absent = BANDWIDTH)
        // and optional "amount" (long, in SUN).
        long frozenBandwidth = 0;
        long frozenEnergy = 0;
        if (root.TryGetProperty("frozenV2", out var frozenV2El) && frozenV2El.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in frozenV2El.EnumerateArray())
            {
                var amount = entry.TryGetProperty("amount", out var amountEl) ? amountEl.GetInt64() : 0;
                if (amount == 0) continue;

                var type = entry.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "" : "";
                if (type.Equals("ENERGY", StringComparison.OrdinalIgnoreCase))
                    frozenEnergy += amount;
                else
                    frozenBandwidth += amount;
            }
        }

        return new AccountInfo(addr, balance, netUsage, energyUsage, createTime,
            frozenBandwidth, frozenEnergy);
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

    public async Task<IReadOnlyList<TransactionInfoDto>> GetAccountTransactionsAsync(
        string address, int limit = 10, CancellationToken ct = default)
    {
        var hexAddress = NormalizeToHex(address);
        var json = await GetAsync($"/v1/accounts/{hexAddress}/transactions?limit={limit}", ct);
        var root = JsonDocument.Parse(json).RootElement;

        var results = new List<TransactionInfoDto>();
        if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var txEl in dataEl.EnumerateArray())
            {
                var txId = txEl.TryGetProperty("txID", out var idEl) ? idEl.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(txId)) continue;

                var blockNumber = txEl.TryGetProperty("blockNumber", out var bnEl) ? bnEl.GetInt64() : 0;
                var blockTs = txEl.TryGetProperty("block_timestamp", out var btsEl) ? btsEl.GetInt64() : 0;

                string contractType = "";
                string ownerAddress = "";
                string toAddress = "";
                long amountSun = 0;
                string? contractAddress = null;
                string? contractData = null;

                if (txEl.TryGetProperty("raw_data", out var rawDataEl)
                    && rawDataEl.TryGetProperty("contract", out var contractsEl)
                    && contractsEl.GetArrayLength() > 0)
                {
                    var contract = contractsEl[0];
                    contractType = contract.TryGetProperty("type", out var typeEl)
                        ? typeEl.GetString() ?? "" : "";

                    if (contract.TryGetProperty("parameter", out var paramEl)
                        && paramEl.TryGetProperty("value", out var valueEl))
                    {
                        ownerAddress = valueEl.TryGetProperty("owner_address", out var ownerEl)
                            ? ownerEl.GetString() ?? "" : "";
                        toAddress = valueEl.TryGetProperty("to_address", out var toEl)
                            ? toEl.GetString() ?? "" : "";
                        amountSun = valueEl.TryGetProperty("amount", out var amtEl)
                            ? amtEl.GetInt64() : 0;
                        contractAddress = valueEl.TryGetProperty("contract_address", out var caEl)
                            ? caEl.GetString() : null;
                        contractData = valueEl.TryGetProperty("data", out var cdEl)
                            ? cdEl.GetString() : null;
                    }
                }

                var contractResult = "";
                if (txEl.TryGetProperty("ret", out var retEl) && retEl.GetArrayLength() > 0)
                {
                    var first = retEl[0];
                    contractResult = first.TryGetProperty("contractRet", out var crEl)
                        ? crEl.GetString() ?? "" : "";
                }

                results.Add(new TransactionInfoDto(
                    txId, blockNumber, blockTs, contractResult, 0, 0, 0,
                    contractType, ownerAddress, toAddress, amountSun, contractAddress, contractData));
            }
        }

        return results;
    }

    public async Task<DelegatedResourceIndex> GetDelegatedResourceAccountIndexAsync(
        string address, CancellationToken ct = default)
    {
        var hexAddress = NormalizeToHex(address);
        var json = await PostAsync("/wallet/getdelegatedresourceaccountindexV2",
            new { value = hexAddress, visible = false }, ct);

        var root = JsonDocument.Parse(json).RootElement;

        var toAddresses = new List<string>();
        var fromAddresses = new List<string>();

        if (root.TryGetProperty("toAccounts", out var toEl) && toEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in toEl.EnumerateArray())
            {
                var addr = item.GetString();
                if (!string.IsNullOrEmpty(addr))
                    toAddresses.Add(addr);
            }
        }

        if (root.TryGetProperty("fromAccounts", out var fromEl) && fromEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in fromEl.EnumerateArray())
            {
                var addr = item.GetString();
                if (!string.IsNullOrEmpty(addr))
                    fromAddresses.Add(addr);
            }
        }

        return new DelegatedResourceIndex(toAddresses, fromAddresses);
    }

    public async Task<IReadOnlyList<DelegatedResourceInfo>> GetDelegatedResourceAsync(
        string fromAddress, string toAddress, CancellationToken ct = default)
    {
        var hexFrom = NormalizeToHex(fromAddress);
        var hexTo = NormalizeToHex(toAddress);
        var json = await PostAsync("/wallet/getdelegatedresourceV2",
            new { fromAddress = hexFrom, toAddress = hexTo, visible = false }, ct);

        var root = JsonDocument.Parse(json).RootElement;
        var results = new List<DelegatedResourceInfo>();

        if (root.TryGetProperty("delegatedResource", out var drEl) && drEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in drEl.EnumerateArray())
            {
                var from = entry.TryGetProperty("from", out var fEl) ? fEl.GetString() ?? "" : "";
                var to = entry.TryGetProperty("to", out var tEl) ? tEl.GetString() ?? "" : "";
                var bw = entry.TryGetProperty("frozen_balance_for_bandwidth", out var bwEl) ? bwEl.GetInt64() : 0;
                var energy = entry.TryGetProperty("frozen_balance_for_energy", out var enEl) ? enEl.GetInt64() : 0;
                results.Add(new DelegatedResourceInfo(from, to, bw, energy));
            }
        }

        return results;
    }

    // --- Private helpers ---

    private async Task<string> GetAsync(string path, CancellationToken ct)
    {
        var url = _baseUrl + path;
        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

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

        List<BlockTransactionInfo>? transactions = null;
        if (root.TryGetProperty("transactions", out var txsEl))
        {
            txCount = txsEl.GetArrayLength();
            transactions = new List<BlockTransactionInfo>(txCount);
            foreach (var txEl in txsEl.EnumerateArray())
            {
                var txInfo = ParseBlockTransaction(txEl);
                if (txInfo != null)
                    transactions.Add(txInfo);
            }
        }

        return new BlockInfo(blockNumber, blockId, timestamp, txCount, rawData, transactions);
    }

    private static BlockTransactionInfo? ParseBlockTransaction(JsonElement txEl)
    {
        var txId = txEl.TryGetProperty("txID", out var txIdEl) ? txIdEl.GetString() ?? "" : "";

        string contractType = "";
        string ownerAddress = "";
        string toAddress = "";
        long amount = 0;
        string? contractAddress = null;
        byte[]? data = null;

        if (txEl.TryGetProperty("raw_data", out var rawDataEl)
            && rawDataEl.TryGetProperty("contract", out var contractsEl)
            && contractsEl.GetArrayLength() > 0)
        {
            var contract = contractsEl[0];
            contractType = contract.TryGetProperty("type", out var typeEl)
                ? typeEl.GetString() ?? "" : "";

            if (contract.TryGetProperty("parameter", out var paramEl)
                && paramEl.TryGetProperty("value", out var valueEl))
            {
                ownerAddress = valueEl.TryGetProperty("owner_address", out var ownerEl)
                    ? ownerEl.GetString() ?? "" : "";
                toAddress = valueEl.TryGetProperty("to_address", out var toEl)
                    ? toEl.GetString() ?? "" : "";
                amount = valueEl.TryGetProperty("amount", out var amtEl)
                    ? amtEl.GetInt64() : 0;
                contractAddress = valueEl.TryGetProperty("contract_address", out var caEl)
                    ? caEl.GetString() : null;
                if (valueEl.TryGetProperty("data", out var dataEl))
                {
                    var dataHex = dataEl.GetString();
                    if (dataHex != null)
                    {
                        try { data = Convert.FromHexString(dataHex); }
                        catch { /* not valid hex, ignore */ }
                    }
                }

                // For TriggerSmartContract, also check call_value
                if (contractType == "TriggerSmartContract" && amount == 0
                    && valueEl.TryGetProperty("call_value", out var cvEl))
                {
                    amount = cvEl.GetInt64();
                }
            }
        }

        return new BlockTransactionInfo(txId, contractType, ownerAddress, toAddress, amount, contractAddress, data);
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

        // Parse contract details from raw_data.contract[0]
        string contractType = "";
        string ownerAddress = "";
        string toAddress = "";
        long amountSun = 0;
        string? contractAddress = null;
        string? contractData = null;

        if (root.TryGetProperty("raw_data", out var rawDataEl)
            && rawDataEl.TryGetProperty("contract", out var contractsEl)
            && contractsEl.GetArrayLength() > 0)
        {
            var contract = contractsEl[0];
            contractType = contract.TryGetProperty("type", out var typeEl)
                ? typeEl.GetString() ?? "" : "";

            if (contract.TryGetProperty("parameter", out var paramEl)
                && paramEl.TryGetProperty("value", out var valueEl))
            {
                ownerAddress = valueEl.TryGetProperty("owner_address", out var ownerEl)
                    ? ownerEl.GetString() ?? "" : "";
                toAddress = valueEl.TryGetProperty("to_address", out var toEl)
                    ? toEl.GetString() ?? "" : "";
                amountSun = valueEl.TryGetProperty("amount", out var amtEl)
                    ? amtEl.GetInt64() : 0;
                contractAddress = valueEl.TryGetProperty("contract_address", out var caEl)
                    ? caEl.GetString() : null;
                contractData = valueEl.TryGetProperty("data", out var dataEl)
                    ? dataEl.GetString() : null;
            }
        }

        return new TransactionInfoDto(id, 0, 0, contractResult, 0, 0, 0,
            contractType, ownerAddress, toAddress, amountSun, contractAddress, contractData);
    }

    private static TransactionInfoDto ParseTransactionInfo(string json, string txId)
    {
        var root = JsonDocument.Parse(json).RootElement;

        var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? txId : txId;
        var blockNum = root.TryGetProperty("blockNumber", out var bnEl) ? bnEl.GetInt64() : 0;
        var blockTs = root.TryGetProperty("blockTimeStamp", out var btsEl) ? btsEl.GetInt64() : 0;
        var fee = root.TryGetProperty("fee", out var feeEl) ? feeEl.GetInt64() : 0;
        long energy = 0, net = 0, energyFee = 0, netFee = 0;
        if (root.TryGetProperty("receipt", out var rcEl))
        {
            energy = rcEl.TryGetProperty("energy_usage_total", out var euEl) ? euEl.GetInt64() : 0;
            net = rcEl.TryGetProperty("net_usage", out var nuEl) ? nuEl.GetInt64() : 0;
            energyFee = rcEl.TryGetProperty("energy_fee", out var efEl) ? efEl.GetInt64() : 0;
            netFee = rcEl.TryGetProperty("net_fee", out var nfEl) ? nfEl.GetInt64() : 0;
        }
        var contractResult = root.TryGetProperty("contractResult", out var crEl)
            && crEl.GetArrayLength() > 0
                ? crEl[0].GetString() ?? "" : "";

        return new TransactionInfoDto(id, blockNum, blockTs, contractResult, fee, energy, net,
            EnergyFee: energyFee, NetFee: netFee);
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
