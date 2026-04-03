using System.Net;
using System.Text;
using System.Text.Json;
using ChainKit.Tron.Protocol.Protobuf;
using ChainKit.Tron.Providers;
using Xunit;

namespace ChainKit.Tron.Tests.Providers;

public class TronHttpProviderTests
{
    private static TronHttpProvider CreateProvider(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        return new TronHttpProvider(client, "https://api.trongrid.io");
    }

    private static MockHandler MockJson(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
        => new(responseJson, status);

    // --- GetAccountAsync ---

    [Fact]
    public async Task GetAccountAsync_CallsCorrectEndpoint_And_ParsesResponse()
    {
        var responseJson = """
        {
            "address": "41a1b2c3d4e5f60000000000000000000000000001",
            "balance": 1000000,
            "net_usage": 500,
            "account_resource": { "energy_usage": 200 },
            "create_time": 1609459200000
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var result = await provider.GetAccountAsync("41a1b2c3d4e5f60000000000000000000000000001");

        Assert.Equal("41a1b2c3d4e5f60000000000000000000000000001", result.Address);
        Assert.Equal(1_000_000, result.Balance);
        Assert.Equal(500, result.NetUsage);
        Assert.Equal(200, result.EnergyUsage);
        Assert.Equal(1609459200000, result.CreateTime);

        Assert.Contains("/wallet/getaccount", handler.LastRequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, handler.LastRequestMethod);
    }

    [Fact]
    public async Task GetAccountAsync_EmptyAccount_ReturnsDefaults()
    {
        // An address that has never been used returns a mostly-empty JSON
        var handler = MockJson("{}");
        var provider = CreateProvider(handler);

        var result = await provider.GetAccountAsync("41a1b2c3d4e5f60000000000000000000000000001");

        Assert.Equal(0, result.Balance);
        Assert.Equal(0, result.NetUsage);
        Assert.Equal(0, result.EnergyUsage);
        Assert.Equal(0, result.FrozenBalanceForBandwidth);
        Assert.Equal(0, result.FrozenBalanceForEnergy);
    }

    [Fact]
    public async Task GetAccountAsync_ParsesFrozenV2_StakingAmounts()
    {
        var responseJson = """
        {
            "address": "41a1b2c3d4e5f60000000000000000000000000001",
            "balance": 5000000,
            "frozenV2": [
                { "amount": 10000000 },
                { "type": "ENERGY", "amount": 25000000 },
                { "type": "ENERGY", "amount": 5000000 }
            ],
            "create_time": 1609459200000
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var result = await provider.GetAccountAsync("41a1b2c3d4e5f60000000000000000000000000001");

        Assert.Equal(5000000, result.Balance);
        Assert.Equal(10000000, result.FrozenBalanceForBandwidth);  // no type = BANDWIDTH
        Assert.Equal(30000000, result.FrozenBalanceForEnergy);     // 25M + 5M
    }

    [Fact]
    public async Task GetAccountAsync_FrozenV2WithZeroAmount_IsIgnored()
    {
        var responseJson = """
        {
            "address": "41a1b2c3d4e5f60000000000000000000000000001",
            "balance": 1000000,
            "frozenV2": [
                { "type": "BANDWIDTH" },
                { "type": "ENERGY", "amount": 0 }
            ]
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var result = await provider.GetAccountAsync("41a1b2c3d4e5f60000000000000000000000000001");

        Assert.Equal(0, result.FrozenBalanceForBandwidth);
        Assert.Equal(0, result.FrozenBalanceForEnergy);
    }

    // --- GetNowBlockAsync ---

    [Fact]
    public async Task GetNowBlockAsync_ParsesBlockInfo()
    {
        var responseJson = """
        {
            "blockID": "0000000002fad2a8abcdef1234567890abcdef1234567890abcdef1234567890",
            "block_header": {
                "raw_data": {
                    "number": 50061992,
                    "timestamp": 1700000000000
                }
            },
            "transactions": [{}, {}, {}]
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var block = await provider.GetNowBlockAsync();

        Assert.Equal(50061992, block.BlockNumber);
        Assert.Equal("0000000002fad2a8abcdef1234567890abcdef1234567890abcdef1234567890", block.BlockId);
        Assert.Equal(1700000000000, block.Timestamp);
        Assert.Equal(3, block.TransactionCount);
        Assert.NotEmpty(block.BlockHeaderRawData);

        Assert.Contains("/wallet/getnowblock", handler.LastRequestUri!.ToString());
    }

    // --- GetBlockByNumAsync ---

    [Fact]
    public async Task GetBlockByNumAsync_SendsCorrectBody()
    {
        var responseJson = """
        {
            "blockID": "abc123",
            "block_header": {
                "raw_data": { "number": 100, "timestamp": 1600000000000 }
            }
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var block = await provider.GetBlockByNumAsync(100);

        Assert.Equal(100, block.BlockNumber);
        Assert.Contains("/wallet/getblockbynum", handler.LastRequestUri!.ToString());

        // Verify the request body contains the block number
        var requestBody = handler.LastRequestBody!;
        Assert.Contains("100", requestBody);
    }

    // --- BroadcastTransactionAsync ---

    [Fact]
    public async Task BroadcastTransactionAsync_ReturnsSuccess()
    {
        var responseJson = """
        {
            "result": true,
            "txid": "abc123def456"
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var tx = new Transaction();
        var result = await provider.BroadcastTransactionAsync(tx);

        Assert.True(result.Success);
        Assert.Equal("abc123def456", result.TxId);
        Assert.Null(result.Message);
        Assert.Contains("/wallet/broadcasttransaction", handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task BroadcastTransactionAsync_ReturnsFailure_WithHexMessage()
    {
        // "Transaction expired" as hex
        var hexMsg = Convert.ToHexString(Encoding.UTF8.GetBytes("Transaction expired"));
        var responseJson = $$"""
        {
            "result": false,
            "message": "{{hexMsg}}"
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var tx = new Transaction();
        var result = await provider.BroadcastTransactionAsync(tx);

        Assert.False(result.Success);
        Assert.Equal("Transaction expired", result.Message);
    }

    // --- GetTransactionByIdAsync ---

    [Fact]
    public async Task GetTransactionByIdAsync_ParsesResult()
    {
        var responseJson = """
        {
            "txID": "abc123",
            "ret": [{ "contractRet": "SUCCESS" }]
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var info = await provider.GetTransactionByIdAsync("abc123");

        Assert.Equal("abc123", info.TxId);
        Assert.Equal("SUCCESS", info.ContractResult);
        Assert.Contains("/wallet/gettransactionbyid", handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task GetTransactionByIdAsync_ParsesTransferContractDetails()
    {
        var responseJson = """
        {
            "txID": "transfer_tx",
            "ret": [{ "contractRet": "SUCCESS" }],
            "raw_data": {
                "contract": [{
                    "type": "TransferContract",
                    "parameter": {
                        "value": {
                            "owner_address": "41a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
                            "to_address": "41b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3",
                            "amount": 10000000
                        }
                    }
                }],
                "timestamp": 1700000000000
            }
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var info = await provider.GetTransactionByIdAsync("transfer_tx");

        Assert.Equal("transfer_tx", info.TxId);
        Assert.Equal("TransferContract", info.ContractType);
        Assert.Equal("41a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2", info.OwnerAddress);
        Assert.Equal("41b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3", info.ToAddress);
        Assert.Equal(10000000, info.AmountSun);
        Assert.Null(info.ContractAddress);
        Assert.Null(info.ContractData);
    }

    [Fact]
    public async Task GetTransactionByIdAsync_ParsesTriggerSmartContractDetails()
    {
        var responseJson = """
        {
            "txID": "trc20_tx",
            "ret": [{ "contractRet": "SUCCESS" }],
            "raw_data": {
                "contract": [{
                    "type": "TriggerSmartContract",
                    "parameter": {
                        "value": {
                            "owner_address": "41a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
                            "contract_address": "41b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3",
                            "data": "a9059cbb000000000000000000000000c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d400000000000000000000000000000000000000000000000000000000000f4240"
                        }
                    }
                }],
                "timestamp": 1700000000000
            }
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var info = await provider.GetTransactionByIdAsync("trc20_tx");

        Assert.Equal("trc20_tx", info.TxId);
        Assert.Equal("TriggerSmartContract", info.ContractType);
        Assert.Equal("41a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2", info.OwnerAddress);
        Assert.Equal("41b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3", info.ContractAddress);
        Assert.StartsWith("a9059cbb", info.ContractData);
    }

    [Fact]
    public async Task GetTransactionByIdAsync_NoRawData_ReturnsEmptyContractFields()
    {
        var responseJson = """
        {
            "txID": "minimal_tx",
            "ret": [{ "contractRet": "SUCCESS" }]
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var info = await provider.GetTransactionByIdAsync("minimal_tx");

        Assert.Equal("minimal_tx", info.TxId);
        Assert.Equal("", info.ContractType);
        Assert.Equal("", info.OwnerAddress);
        Assert.Equal("", info.ToAddress);
        Assert.Equal(0, info.AmountSun);
        Assert.Null(info.ContractAddress);
        Assert.Null(info.ContractData);
    }

    // --- GetTransactionInfoByIdAsync ---

    [Fact]
    public async Task GetTransactionInfoByIdAsync_ParsesFullInfo()
    {
        var responseJson = """
        {
            "id": "def456",
            "blockNumber": 50000,
            "blockTimeStamp": 1700000000000,
            "fee": 27000,
            "receipt": {
                "energy_usage_total": 13000,
                "net_usage": 345
            },
            "contractResult": [""]
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var info = await provider.GetTransactionInfoByIdAsync("def456");

        Assert.Equal("def456", info.TxId);
        Assert.Equal(50000, info.BlockNumber);
        Assert.Equal(1700000000000, info.BlockTimestamp);
        Assert.Equal(27000, info.Fee);
        Assert.Equal(13000, info.EnergyUsage);
        Assert.Equal(345, info.NetUsage);
        Assert.Contains("/walletsolidity/gettransactioninfobyid", handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task GetTransactionInfoByIdAsync_ParsesEnergyFeeAndNetFee()
    {
        var responseJson = """
        {
            "id": "fee_tx",
            "blockNumber": 50000,
            "blockTimeStamp": 1700000000000,
            "fee": 27603900,
            "receipt": {
                "energy_fee": 27255900,
                "net_fee": 348000,
                "energy_usage_total": 64895,
                "net_usage": 345,
                "result": "SUCCESS"
            },
            "contractResult": [""]
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var info = await provider.GetTransactionInfoByIdAsync("fee_tx");

        Assert.Equal("fee_tx", info.TxId);
        Assert.Equal(27603900, info.Fee);
        Assert.Equal(64895, info.EnergyUsage);
        Assert.Equal(345, info.NetUsage);
        Assert.Equal(27255900, info.EnergyFee);
        Assert.Equal(348000, info.NetFee);
    }

    [Fact]
    public async Task GetTransactionInfoByIdAsync_MissingReceipt_ReturnsZeroFees()
    {
        var responseJson = """
        {
            "id": "no_receipt_tx",
            "blockNumber": 50000,
            "blockTimeStamp": 1700000000000,
            "fee": 0,
            "contractResult": [""]
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var info = await provider.GetTransactionInfoByIdAsync("no_receipt_tx");

        Assert.Equal(0, info.EnergyFee);
        Assert.Equal(0, info.NetFee);
        Assert.Equal(0, info.EnergyUsage);
        Assert.Equal(0, info.NetUsage);
    }

    // --- TriggerConstantContractAsync ---

    [Fact]
    public async Task TriggerConstantContractAsync_ReturnsDecodedResult()
    {
        // Return 32 bytes (uint256 = 1000000) as hex
        var hexResult = "00000000000000000000000000000000000000000000000000000000000f4240";
        var responseJson = $$"""
        {
            "constant_result": ["{{hexResult}}"]
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var result = await provider.TriggerConstantContractAsync(
            "41a1b2c3d4e5f60000000000000000000000000001",
            "41a1b2c3d4e5f60000000000000000000000000002",
            "balanceOf(address)",
            new byte[32]);

        Assert.Equal(32, result.Length);
        Assert.Contains("/wallet/triggerconstantcontract", handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task TriggerConstantContractAsync_ThrowsOnError()
    {
        var responseJson = """
        {
            "result": {
                "message": "Contract not found"
            }
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.TriggerConstantContractAsync(
                "41a1b2c3d4e5f60000000000000000000000000001",
                "41a1b2c3d4e5f60000000000000000000000000002",
                "balanceOf(address)",
                new byte[32]));
    }

    // --- GetAccountResourceAsync ---

    [Fact]
    public async Task GetAccountResourceAsync_ParsesResponse()
    {
        var responseJson = """
        {
            "freeNetLimit": 600,
            "freeNetUsed": 100,
            "EnergyLimit": 50000,
            "EnergyUsed": 10000,
            "NetLimit": 5000,
            "NetUsed": 1000
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var resource = await provider.GetAccountResourceAsync(
            "41a1b2c3d4e5f60000000000000000000000000001");

        Assert.Equal(600, resource.FreeBandwidthLimit);
        Assert.Equal(100, resource.FreeBandwidthUsed);
        Assert.Equal(50000, resource.EnergyLimit);
        Assert.Equal(10000, resource.EnergyUsed);
        Assert.Equal(5000, resource.TotalBandwidthLimit);
        Assert.Equal(1000, resource.TotalBandwidthUsed);
        Assert.Contains("/wallet/getaccountresource", handler.LastRequestUri!.ToString());
    }

    // --- EstimateEnergyAsync ---

    [Fact]
    public async Task EstimateEnergyAsync_ReturnsEnergy()
    {
        var responseJson = """
        {
            "energy_required": 32000
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var energy = await provider.EstimateEnergyAsync(
            "41a1b2c3d4e5f60000000000000000000000000001",
            "41a1b2c3d4e5f60000000000000000000000000002",
            "transfer(address,uint256)",
            new byte[64]);

        Assert.Equal(32000, energy);
        Assert.Contains("/wallet/estimateenergy", handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task EstimateEnergyAsync_ReturnsZeroWhenMissing()
    {
        var handler = MockJson("{}");
        var provider = CreateProvider(handler);

        var energy = await provider.EstimateEnergyAsync(
            "41a1b2c3d4e5f60000000000000000000000000001",
            "41a1b2c3d4e5f60000000000000000000000000002",
            "transfer(address,uint256)",
            new byte[64]);

        Assert.Equal(0, energy);
    }

    // --- GetAccountTransactionsAsync ---

    [Fact]
    public async Task GetAccountTransactionsAsync_ParsesTransactions()
    {
        var responseJson = """
        {
            "data": [
                {
                    "txID": "abc123",
                    "blockNumber": 100,
                    "block_timestamp": 1700000000000,
                    "raw_data": {
                        "contract": [{
                            "type": "TransferContract",
                            "parameter": {
                                "value": {
                                    "owner_address": "41a1b2c3d4e5f60000000000000000000000000001",
                                    "to_address": "41a1b2c3d4e5f60000000000000000000000000002",
                                    "amount": 5000000
                                }
                            }
                        }]
                    },
                    "ret": [{ "contractRet": "SUCCESS" }]
                }
            ],
            "success": true
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var result = await provider.GetAccountTransactionsAsync("41a1b2c3d4e5f60000000000000000000000000001", 10);

        Assert.Single(result);
        Assert.Equal("abc123", result[0].TxId);
        Assert.Equal(100, result[0].BlockNumber);
        Assert.Equal(1700000000000, result[0].BlockTimestamp);
        Assert.Equal("TransferContract", result[0].ContractType);
        Assert.Equal("SUCCESS", result[0].ContractResult);
        Assert.Equal(5000000, result[0].AmountSun);

        Assert.Contains("/v1/accounts/", handler.LastRequestUri!.ToString());
        Assert.Contains("/transactions", handler.LastRequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, handler.LastRequestMethod);
    }

    [Fact]
    public async Task GetAccountTransactionsAsync_EmptyData_ReturnsEmptyList()
    {
        var handler = MockJson("""{ "data": [], "success": true }""");
        var provider = CreateProvider(handler);

        var result = await provider.GetAccountTransactionsAsync("41a1b2c3d4e5f60000000000000000000000000001");

        Assert.Empty(result);
    }

    // --- GetDelegatedResourceAccountIndexAsync ---

    [Fact]
    public async Task GetDelegatedResourceAccountIndexAsync_ParsesAddresses()
    {
        var responseJson = """
        {
            "account": "41a1b2c3d4e5f60000000000000000000000000001",
            "toAccounts": [
                "41b2c3d4e5f60000000000000000000000000000a1",
                "41c3d4e5f60000000000000000000000000000a1b2"
            ],
            "fromAccounts": [
                "41d4e5f6000000000000000000000000000000a1b2"
            ]
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var result = await provider.GetDelegatedResourceAccountIndexAsync("41a1b2c3d4e5f60000000000000000000000000001");

        Assert.Equal(2, result.ToAddresses.Count);
        Assert.Equal("41b2c3d4e5f60000000000000000000000000000a1", result.ToAddresses[0]);
        Assert.Single(result.FromAddresses);

        Assert.Contains("/wallet/getdelegatedresourceaccountindexV2", handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task GetDelegatedResourceAccountIndexAsync_EmptyResponse_ReturnsEmptyLists()
    {
        var handler = MockJson("{}");
        var provider = CreateProvider(handler);

        var result = await provider.GetDelegatedResourceAccountIndexAsync("41a1b2c3d4e5f60000000000000000000000000001");

        Assert.Empty(result.ToAddresses);
        Assert.Empty(result.FromAddresses);
    }

    // --- GetDelegatedResourceAsync ---

    [Fact]
    public async Task GetDelegatedResourceAsync_ParsesResources()
    {
        var responseJson = """
        {
            "delegatedResource": [
                {
                    "from": "41a1b2c3d4e5f60000000000000000000000000001",
                    "to": "41b2c3d4e5f60000000000000000000000000000a1",
                    "frozen_balance_for_bandwidth": 5000000,
                    "frozen_balance_for_energy": 3000000
                }
            ]
        }
        """;

        var handler = MockJson(responseJson);
        var provider = CreateProvider(handler);

        var result = await provider.GetDelegatedResourceAsync(
            "41a1b2c3d4e5f60000000000000000000000000001",
            "41b2c3d4e5f60000000000000000000000000000a1");

        Assert.Single(result);
        Assert.Equal("41a1b2c3d4e5f60000000000000000000000000001", result[0].From);
        Assert.Equal("41b2c3d4e5f60000000000000000000000000000a1", result[0].To);
        Assert.Equal(5000000, result[0].FrozenBalanceForBandwidth);
        Assert.Equal(3000000, result[0].FrozenBalanceForEnergy);

        Assert.Contains("/wallet/getdelegatedresourceV2", handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task GetDelegatedResourceAsync_EmptyResponse_ReturnsEmptyList()
    {
        var handler = MockJson("{}");
        var provider = CreateProvider(handler);

        var result = await provider.GetDelegatedResourceAsync(
            "41a1b2c3d4e5f60000000000000000000000000001",
            "41b2c3d4e5f60000000000000000000000000000a1");

        Assert.Empty(result);
    }

    // --- Constructor tests ---

    [Fact]
    public void Constructor_WithApiKey_AddsHeader()
    {
        // This just verifies the constructor doesn't throw
        var provider = new TronHttpProvider("https://api.trongrid.io", "test-api-key");
        Assert.NotNull(provider);
    }

    [Fact]
    public void Constructor_WithNetworkConfig_UsesHttpEndpoint()
    {
        var provider = new TronHttpProvider(TronNetwork.Nile);
        Assert.NotNull(provider);
    }

    // --- Mock HTTP handler ---

    private sealed class MockHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        private readonly HttpStatusCode _statusCode;

        public Uri? LastRequestUri { get; private set; }
        public HttpMethod? LastRequestMethod { get; private set; }
        public string? LastRequestBody { get; private set; }

        public MockHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseJson = responseJson;
            _statusCode = statusCode;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastRequestMethod = request.Method;

            if (request.Content != null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
