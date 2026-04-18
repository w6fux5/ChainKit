namespace ChainKit.Evm.Watching;

/// <summary>
/// Snapshot of one EVM node health check. Adds <see cref="ChainIdMatch"/> compared to the Tron equivalent.
/// When <see cref="Reachable"/> is false, all numeric fields are null and <see cref="Error"/> holds the reason.
/// </summary>
/// <param name="Timestamp">When the check ran (UTC).</param>
/// <param name="Reachable">Whether the node responded successfully to the probe.</param>
/// <param name="Latency">Round-trip duration of the probe. Populated even on failure.</param>
/// <param name="BlockNumber">Latest block number; null when <see cref="Reachable"/> is false.</param>
/// <param name="BlockAge">How old the latest block is (UtcNow - block timestamp, clamped to &gt;= 0); null when <see cref="Reachable"/> is false.</param>
/// <param name="ChainIdMatch">Whether the node's reported chain ID matches the configured <c>EvmNetworkConfig.ChainId</c>;
/// null if chain ID has not yet been successfully fetched.</param>
/// <param name="Error">Exception message when <see cref="Reachable"/> is false; null on success.</param>
public sealed record EvmNodeHealthReport(
    DateTimeOffset Timestamp,
    bool Reachable,
    TimeSpan Latency,
    long? BlockNumber,
    TimeSpan? BlockAge,
    bool? ChainIdMatch,
    string? Error);
