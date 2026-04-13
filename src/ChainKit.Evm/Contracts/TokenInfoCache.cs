using System.Collections.Concurrent;
using ChainKit.Evm.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChainKit.Evm.Contracts;

/// <summary>
/// Three-layer cache for ERC20 token metadata, keyed by {chainId}:{normalizedAddress}:
/// 1. Built-in known tokens (USDT, USDC per network) — zero latency.
/// 2. In-memory ConcurrentDictionary — zero latency for previously resolved contracts.
/// 3. Contract call via resolver callback — result cached permanently.
/// </summary>
public class TokenInfoCache
{
    private readonly ConcurrentDictionary<string, TokenInfo> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _logger;

    private static readonly Dictionary<string, TokenInfo> KnownTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        // Ethereum Mainnet
        ["1:0xdac17f958d2ee523a2206206994597c13d831ec7"] = new("0xdAC17F958D2ee523a2206206994597C13D831ec7", "Tether USD", "USDT", 6, default, null),
        ["1:0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48"] = new("0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48", "USD Coin", "USDC", 6, default, null),
        // Polygon
        ["137:0xc2132d05d31c914a87c6611c10748aeb04b58e8f"] = new("0xc2132D05D31c914a87C6611C10748AEb04B58e8F", "Tether USD", "USDT", 6, default, null),
        ["137:0x3c499c542cef5e3811e1192ce70d8cc03d5c3359"] = new("0x3c499c542cEF5E3811e1192ce70d8cC03d5c3359", "USD Coin", "USDC", 6, default, null),
    };

    /// <summary>
    /// Creates a new TokenInfoCache instance.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics. Defaults to NullLogger.</param>
    public TokenInfoCache(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Returns cached token info or resolves it from the contract via the provided resolver callback.
    /// Checks known tokens first, then memory cache, then calls the resolver.
    /// The result is cached permanently (name/symbol/decimals never change).
    /// </summary>
    /// <param name="contractAddress">The ERC20 contract address (0x-prefixed).</param>
    /// <param name="chainId">The EIP-155 chain ID.</param>
    /// <param name="resolveFromContract">Callback to resolve token info on cache miss.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The token info, or null if resolution fails.</returns>
    public async Task<TokenInfo?> GetOrResolveAsync(
        string contractAddress, long chainId,
        Func<string, Task<TokenInfo?>> resolveFromContract,
        CancellationToken ct = default)
    {
        var key = $"{chainId}:{contractAddress.ToLowerInvariant()}";

        if (KnownTokens.TryGetValue(key, out var known))
            return known;
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            var resolved = await resolveFromContract(contractAddress);
            if (resolved != null)
                _cache[key] = resolved;
            return resolved;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Token info resolution failed for {Address} on chain {ChainId}", contractAddress, chainId);
            return null;
        }
    }
}
