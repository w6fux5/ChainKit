using ChainKit.Tron.Protocol.Protobuf;
using ChainKit.Tron.Providers;
using Grpc.Core;
using Xunit;

namespace ChainKit.Tron.Tests.Providers;

public class TronGrpcProviderTests
{
    // --- Constructor tests ---

    [Fact]
    public void Constructor_WithFullNodeEndpoint_Succeeds()
    {
        using var provider = new TronGrpcProvider("grpc.trongrid.io:50051");
        Assert.NotNull(provider);
    }

    [Fact]
    public void Constructor_WithBothEndpoints_Succeeds()
    {
        using var provider = new TronGrpcProvider(
            "grpc.trongrid.io:50051",
            "grpc.trongrid.io:50061");
        Assert.NotNull(provider);
    }

    [Fact]
    public void Constructor_WithNetworkConfig_Succeeds()
    {
        using var provider = new TronGrpcProvider(TronNetwork.Mainnet);
        Assert.NotNull(provider);
    }

    [Fact]
    public void Constructor_WithNileTestnet_Succeeds()
    {
        using var provider = new TronGrpcProvider(TronNetwork.Nile);
        Assert.NotNull(provider);
    }

    [Fact]
    public void Constructor_WithShastaTestnet_Succeeds()
    {
        using var provider = new TronGrpcProvider(TronNetwork.Shasta);
        Assert.NotNull(provider);
    }

    [Fact]
    public void Constructor_NullEndpoint_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TronGrpcProvider(fullNodeEndpoint: null!));
    }

    [Fact]
    public void Constructor_EmptyEndpoint_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new TronGrpcProvider(""));
    }

    [Fact]
    public void Constructor_WhitespaceEndpoint_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new TronGrpcProvider("   "));
    }

    // --- Solidity routing tests ---

    [Fact]
    public async Task GetTransactionInfoByIdAsync_WithSolidityInvoker_UsesSolidityService()
    {
        var fullNodeInvoker = new TrackingCallInvoker("FullNode");
        var solidityInvoker = new TrackingCallInvoker("Solidity");

        var provider = new TronGrpcProvider(fullNodeInvoker, solidityInvoker);

        // This will throw because our mock invoker doesn't actually call gRPC,
        // but we can verify which invoker was used before the throw.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetTransactionInfoByIdAsync(
                "0000000000000000000000000000000000000000000000000000000000000001"));

        Assert.Equal("Solidity", ex.Message);
    }

    [Fact]
    public async Task GetTransactionInfoByIdAsync_WithoutSolidityInvoker_FallsBackToFullNode()
    {
        var fullNodeInvoker = new TrackingCallInvoker("FullNode");

        var provider = new TronGrpcProvider(fullNodeInvoker, solidityInvoker: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetTransactionInfoByIdAsync(
                "0000000000000000000000000000000000000000000000000000000000000001"));

        Assert.Equal("FullNode", ex.Message);
    }

    [Fact]
    public async Task GetAccountAsync_UsesFullNodeInvoker()
    {
        var fullNodeInvoker = new TrackingCallInvoker("FullNode");
        var solidityInvoker = new TrackingCallInvoker("Solidity");

        var provider = new TronGrpcProvider(fullNodeInvoker, solidityInvoker);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetAccountAsync("41a1b2c3d4e5f60000000000000000000000000001"));

        Assert.Equal("FullNode", ex.Message);
    }

    [Fact]
    public async Task GetNowBlockAsync_UsesFullNodeInvoker()
    {
        var fullNodeInvoker = new TrackingCallInvoker("FullNode");
        var provider = new TronGrpcProvider(fullNodeInvoker);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetNowBlockAsync());

        Assert.Equal("FullNode", ex.Message);
    }

    // --- CreateTransactionAsync throws NotSupportedException ---

    [Fact]
    public async Task CreateTransactionAsync_ThrowsNotSupported()
    {
        var provider = new TronGrpcProvider(new TrackingCallInvoker("FullNode"));

        await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.CreateTransactionAsync(new Transaction()));
    }

    // --- Protobuf encoding helper tests ---

    [Fact]
    public void EncodeField_ProducesValidProtobuf()
    {
        // Field 1, wire type 2 (length-delimited) = tag byte 0x0A
        var data = new byte[] { 0x41, 0x42, 0x43 };
        var encoded = TronGrpcProvider.EncodeField(1, data);

        Assert.Equal(0x0A, encoded[0]); // tag: (1 << 3) | 2 = 10 = 0x0A
        Assert.Equal(3, encoded[1]);    // length = 3
        Assert.Equal(0x41, encoded[2]);
        Assert.Equal(0x42, encoded[3]);
        Assert.Equal(0x43, encoded[4]);
        Assert.Equal(5, encoded.Length);
    }

    [Fact]
    public void EncodeVarintField_ProducesValidProtobuf()
    {
        // Field 1, wire type 0 (varint) = tag byte 0x08
        var encoded = TronGrpcProvider.EncodeVarintField(1, 150);

        Assert.Equal(0x08, encoded[0]); // tag: (1 << 3) | 0 = 8
        // 150 = 0x96 = 10010110 => varint: [10010110, 00000001]
        Assert.Equal(0x96, encoded[1]);
        Assert.Equal(0x01, encoded[2]);
        Assert.Equal(3, encoded.Length);
    }

    [Fact]
    public void EncodeVarintField_Zero_ProducesMinimalEncoding()
    {
        var encoded = TronGrpcProvider.EncodeVarintField(1, 0);

        Assert.Equal(0x08, encoded[0]); // tag
        Assert.Equal(0x00, encoded[1]); // value = 0
        Assert.Equal(2, encoded.Length);
    }

    [Fact]
    public void ParseVarintField_ReadsCorrectValue()
    {
        // Encode field 1 = 42
        var data = TronGrpcProvider.EncodeVarintField(1, 42);
        var value = TronGrpcProvider.ParseVarintField(data, 1);

        Assert.Equal(42, value);
    }

    [Fact]
    public void ParseVarintField_MissingField_ReturnsZero()
    {
        // Encode field 2 = 100, then ask for field 1
        var data = TronGrpcProvider.EncodeVarintField(2, 100);
        var value = TronGrpcProvider.ParseVarintField(data, 1);

        Assert.Equal(0, value);
    }

    [Fact]
    public void ParseBytesField_ReadsCorrectValue()
    {
        var originalData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var encoded = TronGrpcProvider.EncodeField(1, originalData);
        var parsed = TronGrpcProvider.ParseBytesField(encoded, 1);

        Assert.Equal(originalData, parsed);
    }

    [Fact]
    public void ParseBytesField_MissingField_ReturnsEmpty()
    {
        var encoded = TronGrpcProvider.EncodeField(2, new byte[] { 0x01 });
        var parsed = TronGrpcProvider.ParseBytesField(encoded, 1);

        Assert.Empty(parsed);
    }

    [Fact]
    public void ParseRepeatedBytesField_ReadsMultipleValues()
    {
        // Encode field 4 twice (simulating repeated bytes constant_result)
        var val1 = new byte[] { 0x01, 0x02 };
        var val2 = new byte[] { 0x03, 0x04, 0x05 };
        var enc1 = TronGrpcProvider.EncodeField(4, val1);
        var enc2 = TronGrpcProvider.EncodeField(4, val2);

        var combined = new byte[enc1.Length + enc2.Length];
        Array.Copy(enc1, 0, combined, 0, enc1.Length);
        Array.Copy(enc2, 0, combined, enc1.Length, enc2.Length);

        var results = TronGrpcProvider.ParseRepeatedBytesField(combined, 4);

        Assert.Equal(2, results.Count);
        Assert.Equal(val1, results[0]);
        Assert.Equal(val2, results[1]);
    }

    [Fact]
    public void ParseVarintField_SkipsLengthDelimitedFields()
    {
        // Encode: field 1 (bytes) = [0x01, 0x02], field 2 (varint) = 99
        var bytesField = TronGrpcProvider.EncodeField(1, new byte[] { 0x01, 0x02 });
        var varintField = TronGrpcProvider.EncodeVarintField(2, 99);

        var combined = new byte[bytesField.Length + varintField.Length];
        Array.Copy(bytesField, 0, combined, 0, bytesField.Length);
        Array.Copy(varintField, 0, combined, bytesField.Length, varintField.Length);

        var value = TronGrpcProvider.ParseVarintField(combined, 2);
        Assert.Equal(99, value);
    }

    [Fact]
    public void RoundTrip_MultipleFieldTypes()
    {
        // Build a message with: field 1 (bytes) = address, field 5 (varint) = balance, field 8 (varint) = netUsage
        var addressBytes = Convert.FromHexString("41a1b2c3d4e5f60000000000000000000000000001");
        var f1 = TronGrpcProvider.EncodeField(1, addressBytes);
        var f5 = TronGrpcProvider.EncodeVarintField(5, 1_000_000);
        var f8 = TronGrpcProvider.EncodeVarintField(8, 500);

        var combined = new byte[f1.Length + f5.Length + f8.Length];
        Array.Copy(f1, 0, combined, 0, f1.Length);
        Array.Copy(f5, 0, combined, f1.Length, f5.Length);
        Array.Copy(f8, 0, combined, f1.Length + f5.Length, f8.Length);

        Assert.Equal(addressBytes, TronGrpcProvider.ParseBytesField(combined, 1));
        Assert.Equal(1_000_000, TronGrpcProvider.ParseVarintField(combined, 5));
        Assert.Equal(500, TronGrpcProvider.ParseVarintField(combined, 8));
    }

    // --- Dispose tests ---

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var provider = new TronGrpcProvider("grpc.trongrid.io:50051");
        provider.Dispose();
        provider.Dispose(); // should not throw
    }

    // --- ITronProvider interface compliance ---

    [Fact]
    public void ImplementsITronProvider()
    {
        using var provider = new TronGrpcProvider("grpc.trongrid.io:50051");
        Assert.IsAssignableFrom<ITronProvider>(provider);
    }

    [Fact]
    public void ImplementsIDisposable()
    {
        using var provider = new TronGrpcProvider("grpc.trongrid.io:50051");
        Assert.IsAssignableFrom<IDisposable>(provider);
    }

    // --- Mock call invoker for testing routing ---

    /// <summary>
    /// A call invoker that throws with its name, allowing tests to verify
    /// which invoker (full node vs solidity) was used for a given call.
    /// </summary>
    private sealed class TrackingCallInvoker : CallInvoker
    {
        private readonly string _name;

        public TrackingCallInvoker(string name) => _name = name;

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host,
            CallOptions options, TRequest request)
            => throw new InvalidOperationException(_name);

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host,
            CallOptions options, TRequest request)
        {
            // Return a faulted task that indicates which invoker was called
            var tcs = new TaskCompletionSource<TResponse>();
            tcs.SetException(new InvalidOperationException(_name));

            return new AsyncUnaryCall<TResponse>(
                tcs.Task,
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host,
            CallOptions options, TRequest request)
            => throw new InvalidOperationException(_name);

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host,
            CallOptions options)
            => throw new InvalidOperationException(_name);

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host,
            CallOptions options)
            => throw new InvalidOperationException(_name);
    }
}
