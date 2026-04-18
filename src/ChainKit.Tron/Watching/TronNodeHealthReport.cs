namespace ChainKit.Tron.Watching;

/// <summary>
/// Snapshot of one node health check. <see cref="Reachable"/> tells you whether the probe
/// succeeded; when false, all numeric fields are null and <see cref="Error"/> holds the reason.
/// </summary>
/// <param name="Timestamp">When the check ran (UTC).</param>
/// <param name="Reachable">Whether the node responded successfully to the probe.</param>
/// <param name="Latency">Round-trip duration of the probe. Populated even on failure.</param>
/// <param name="BlockNumber">Latest block number; null when <see cref="Reachable"/> is false.</param>
/// <param name="BlockAge">How old the latest block is (UtcNow - block timestamp, clamped to &gt;= 0); null when <see cref="Reachable"/> is false.</param>
/// <param name="Error">Exception message when <see cref="Reachable"/> is false; null on success.</param>
public sealed record TronNodeHealthReport(
    DateTimeOffset Timestamp,
    bool Reachable,
    TimeSpan Latency,
    long? BlockNumber,
    TimeSpan? BlockAge,
    string? Error);
