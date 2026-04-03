namespace ChainKit.Tron.Providers;

public record TronNetworkConfig(
    string HttpEndpoint,
    string GrpcFullNodeEndpoint,
    string? GrpcSolidityEndpoint = null);

public static class TronNetwork
{
    public static readonly TronNetworkConfig Mainnet = new(
        "https://api.trongrid.io",
        "grpc.trongrid.io:50051",
        "grpc.trongrid.io:50061");

    public static readonly TronNetworkConfig Nile = new(
        "https://nile.trongrid.io",
        "grpc.nile.trongrid.io:50051",
        "grpc.nile.trongrid.io:50061");

    public static readonly TronNetworkConfig Shasta = new(
        "https://api.shasta.trongrid.io",
        "grpc.shasta.trongrid.io:50051",
        "grpc.shasta.trongrid.io:50061");
}
