using System.Collections.Concurrent;
using ChainKit.Tron.Crypto;
using ChainKit.Tron.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChainKit.Tron.Contracts;

/// <summary>
/// Resolved token metadata (symbol and decimals). Immutable once created.
/// </summary>
public record TokenInfo(string Symbol, int Decimals);

/// <summary>
/// Three-layer cache for TRC20 token metadata resolution:
/// 1. Built-in known tokens (USDT, etc.) — zero latency.
/// 2. In-memory cache — zero latency for previously resolved contracts.
/// 3. On-chain contract call (symbol() + decimals()) — result cached permanently.
/// </summary>
public class TokenInfoCache
{
    private readonly ConcurrentDictionary<string, TokenInfo> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _logger;

    public TokenInfoCache(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    // Well-known mainnet TRC20 contract addresses (hex with 41-prefix, lowercase).
    // USDT: TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t
    private static readonly Dictionary<string, TokenInfo> KnownTokens =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["41a614f803b6fd780986a42c78ec9c7f77e6ded13c"] = new("USDT", 6),
        };

    /// <summary>
    /// Attempts to get token info from layer 1 (known tokens) or layer 2 (cache).
    /// Returns null if the contract has not been resolved yet.
    /// </summary>
    public TokenInfo? Get(string contractAddress)
    {
        var key = NormalizeAddress(contractAddress);

        if (KnownTokens.TryGetValue(key, out var known))
            return known;

        if (_cache.TryGetValue(key, out var cached))
            return cached;

        return null;
    }

    /// <summary>
    /// Returns cached token info or resolves it from the contract via <paramref name="provider"/>.
    /// The result is cached permanently (symbol/decimals never change).
    /// </summary>
    public async Task<TokenInfo> GetOrResolveAsync(
        string contractAddress, ITronProvider provider, CancellationToken ct = default)
    {
        var key = NormalizeAddress(contractAddress);

        // Layer 1 + 2
        var existing = Get(contractAddress);
        if (existing is not null)
            return existing;

        // Layer 3: on-chain resolution
        var info = await ResolveFromContractAsync(key, provider, ct);
        _cache[key] = info;
        return info;
    }

    /// <summary>
    /// Manually adds or overwrites a cache entry.
    /// </summary>
    public void Set(string contractAddress, TokenInfo info)
    {
        _cache[NormalizeAddress(contractAddress)] = info;
    }

    // --- Internal helpers ---

    private async Task<TokenInfo> ResolveFromContractAsync(
        string contractHex, ITronProvider provider, CancellationToken ct)
    {
        string symbol = "";
        int decimals = 0;

        try
        {
            var symbolResult = await provider.TriggerConstantContractAsync(
                contractHex, contractHex, "symbol()", Array.Empty<byte>(), ct);
            if (symbolResult.Length >= 64)
                symbol = AbiEncoder.DecodeString(symbolResult);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "symbol() call failed for contract {Contract}", contractHex); }

        try
        {
            var decimalsResult = await provider.TriggerConstantContractAsync(
                contractHex, contractHex, "decimals()", Array.Empty<byte>(), ct);
            if (decimalsResult.Length >= 32)
                decimals = (int)AbiEncoder.DecodeUint256(decimalsResult);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "decimals() call failed for contract {Contract}", contractHex); }

        return new TokenInfo(symbol, decimals);
    }

    internal static string NormalizeAddress(string address)
    {
        if (address.StartsWith('T'))
            return TronAddress.ToHex(address);
        return address.ToLowerInvariant();
    }
}
