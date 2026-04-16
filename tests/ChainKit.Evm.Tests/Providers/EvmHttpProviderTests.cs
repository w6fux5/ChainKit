using System.Net;
using System.Text;
using ChainKit.Evm.Providers;
using Xunit;

namespace ChainKit.Evm.Tests.Providers;

public class EvmHttpProviderTests
{
    private const string FakeRpcUrl = "http://localhost:8545";

    private static MockHandler SetupRpcResponse(string method, string resultJson)
    {
        // Wrap the result in a full JSON-RPC 2.0 response envelope
        var body = $$"""{"jsonrpc":"2.0","id":1,"result":{{resultJson}}}""";
        return new MockHandler(body);
    }

    private static EvmHttpProvider CreateProvider(MockHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new EvmHttpProvider(httpClient, FakeRpcUrl);
    }

    // === GetChainIdAsync ===

    [Fact]
    public async Task GetChainIdAsync_ReturnsDecodedChainId()
    {
        // eth_chainId returns a 0x-prefixed hex string; 0x1 = 1 (Ethereum mainnet)
        var handler = SetupRpcResponse("eth_chainId", "\"0x1\"");

        using var provider = CreateProvider(handler);
        var chainId = await provider.GetChainIdAsync();

        Assert.Equal(1L, chainId);
    }

    [Fact]
    public async Task GetChainIdAsync_LargeChainId_DecodesCorrectly()
    {
        // 0x89 = 137 (Polygon mainnet)
        var handler = SetupRpcResponse("eth_chainId", "\"0x89\"");

        using var provider = CreateProvider(handler);
        var chainId = await provider.GetChainIdAsync();

        Assert.Equal(137L, chainId);
    }

    // --- MockHandler ---

    private sealed class MockHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        private readonly HttpStatusCode _statusCode;

        public MockHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseJson = responseJson;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
