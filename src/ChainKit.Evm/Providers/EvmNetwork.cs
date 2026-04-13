namespace ChainKit.Evm.Providers;

/// <summary>
/// EVM network configuration — identifies a specific chain with its RPC endpoint and chain ID.
/// </summary>
/// <param name="RpcUrl">The JSON-RPC endpoint URL.</param>
/// <param name="ChainId">The EIP-155 chain ID.</param>
/// <param name="Name">Human-readable network name.</param>
/// <param name="NativeCurrency">The native currency symbol (e.g. ETH, POL).</param>
/// <param name="Decimals">The number of decimals for the native currency (default 18).</param>
public record EvmNetworkConfig(string RpcUrl, long ChainId, string Name, string NativeCurrency, int Decimals = 18);

/// <summary>
/// Pre-configured EVM networks.
/// Default RPC URLs are public endpoints. For production, use your own node or a provider like Alchemy/Infura.
/// </summary>
public static class EvmNetwork
{
    /// <summary>Ethereum Mainnet (Chain ID 1).</summary>
    public static readonly EvmNetworkConfig EthereumMainnet = new("https://eth.llamarpc.com", 1, "Ethereum Mainnet", "ETH");

    /// <summary>Sepolia testnet (Chain ID 11155111).</summary>
    public static readonly EvmNetworkConfig Sepolia = new("https://rpc.sepolia.org", 11155111, "Sepolia", "ETH");

    /// <summary>Polygon Mainnet (Chain ID 137).</summary>
    public static readonly EvmNetworkConfig PolygonMainnet = new("https://polygon-rpc.com", 137, "Polygon", "POL");

    /// <summary>Polygon Amoy testnet (Chain ID 80002).</summary>
    public static readonly EvmNetworkConfig PolygonAmoy = new("https://rpc-amoy.polygon.technology", 80002, "Polygon Amoy", "POL");

    /// <summary>
    /// Creates a custom network configuration.
    /// </summary>
    public static EvmNetworkConfig Custom(string rpcUrl, long chainId, string name, string nativeCurrency, int decimals = 18)
        => new(rpcUrl, chainId, name, nativeCurrency, decimals);
}
