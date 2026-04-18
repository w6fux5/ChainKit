# Node Health Watcher — Design Spec

**Date:** 2026-04-16
**Status:** Approved — ready for implementation plan

## Problem

Applications using ChainKit have no first-class way to observe whether the
underlying Tron / EVM node is healthy. Callers today would have to build
ad-hoc probes (call `GetBlockNumberAsync` in a loop, catch exceptions, time
themselves). This is repetitive, easy to get wrong, and doesn't reuse the
existing ChainKit subscription patterns.

## Goals

- First-class, per-chain health observation built into the SDK
- Event-based API mirroring the existing Watcher pattern
  (`TronTransactionWatcher` / `EvmTransactionWatcher`)
- Configurable polling interval
- Report raw metrics; let the caller decide what "healthy" means

## Non-Goals (out of scope for this iteration)

- Built-in thresholds or health-state judgement inside the SDK
- State-change-only events (caller can layer this on top if wanted)
- Multi-provider load-balancer / failover logic
- Tron Solidity Node independent probing (Full Node only for now)
- Tron chain-ID equivalent checks (Tron protocol has no native chainId)
- Human-readable status-page rendering

## Architecture

### Layout

```
src/ChainKit.Tron/Watching/
├── TronNodeHealthWatcher.cs
├── TronNodeHealthReport.cs
└── TronNodeHealthCheckedEventArgs.cs

src/ChainKit.Evm/Watching/
├── EvmNodeHealthWatcher.cs
├── EvmNodeHealthReport.cs
└── EvmNodeHealthCheckedEventArgs.cs
```

Per-chain types, symmetric API, chain-specific fields diverge where required
(EVM has `ChainIdMatch`; Tron does not).

### Public API

**Tron**

```csharp
public sealed class TronNodeHealthWatcher : IAsyncDisposable
{
    public TronNodeHealthWatcher(
        ITronProvider provider,
        int intervalMs = 5000,
        ILogger<TronNodeHealthWatcher>? logger = null);

    public event EventHandler<TronNodeHealthCheckedEventArgs>? OnHealthChecked;

    public Task StartAsync(CancellationToken ct = default);
    public Task StopAsync();
    public ValueTask DisposeAsync();
}

public sealed record TronNodeHealthReport(
    DateTimeOffset Timestamp,   // When the check ran
    bool Reachable,             // Did the probe succeed?
    TimeSpan Latency,           // Round-trip duration
    long? BlockNumber,          // null when !Reachable
    TimeSpan? BlockAge,         // UtcNow - block.timestamp; null when !Reachable
    string? Error);             // Exception message when !Reachable

public sealed class TronNodeHealthCheckedEventArgs(TronNodeHealthReport report) : EventArgs
{
    public TronNodeHealthReport Report { get; } = report;
}
```

**EVM**

```csharp
public sealed class EvmNodeHealthWatcher : IAsyncDisposable
{
    public EvmNodeHealthWatcher(
        IEvmProvider provider,
        EvmNetworkConfig network,         // for ChainId comparison
        int intervalMs = 5000,
        ILogger<EvmNodeHealthWatcher>? logger = null);

    public event EventHandler<EvmNodeHealthCheckedEventArgs>? OnHealthChecked;

    public Task StartAsync(CancellationToken ct = default);
    public Task StopAsync();
    public ValueTask DisposeAsync();
}

public sealed record EvmNodeHealthReport(
    DateTimeOffset Timestamp,
    bool Reachable,
    TimeSpan Latency,
    long? BlockNumber,
    TimeSpan? BlockAge,
    bool? ChainIdMatch,   // null when unable to determine (e.g. node unreachable)
    string? Error);

public sealed class EvmNodeHealthCheckedEventArgs(EvmNodeHealthReport report) : EventArgs
{
    public EvmNodeHealthReport Report { get; } = report;
}
```

### Usage example

```csharp
using var watcher = new EvmNodeHealthWatcher(provider, network, intervalMs: 5000);
watcher.OnHealthChecked += (_, e) =>
{
    if (!e.Report.Reachable)
        logger.Warn($"Node unreachable: {e.Report.Error}");
    else if (e.Report.Latency > TimeSpan.FromSeconds(3))
        logger.Warn($"Node slow: {e.Report.Latency.TotalMilliseconds}ms");
};
await watcher.StartAsync(ct);
// ... app runs ...
await watcher.StopAsync();
```

## Prerequisites (additive SDK changes)

### `IEvmProvider.GetChainIdAsync` (new)

`IEvmProvider` currently has no method for `eth_chainId`. Since the watcher
needs to probe the node's chain ID to compute `ChainIdMatch`, extend the
interface:

```csharp
/// <summary>
/// Gets the chain ID reported by the node via eth_chainId.
/// </summary>
Task<long> GetChainIdAsync(CancellationToken ct = default);
```

Implementation in `EvmHttpProvider` is a straightforward JSON-RPC call returning
the hex-decoded `result` as `long`.

This is additive; no existing callers are affected.

`ITronProvider` needs no additions — the watcher uses the existing
`GetNowBlockAsync` only.

## Poll Loop

```
StartAsync:
  1. If already running: return
  2. Create internal CancellationTokenSource
  3. Launch background Task (below)
  4. Return

Background Task (every intervalMs):
  1. startedAt = DateTimeOffset.UtcNow
  2. Try to probe provider:
     - Tron: GetNowBlockAsync() → block.Number + block.Timestamp
     - EVM:  GetBlockNumberAsync() + GetBlockByNumberAsync(n, false)
             + eth_chainId (cached after first successful poll)
  3. Latency = UtcNow - startedAt
  4. BlockAge = UtcNow - block.timestamp (clamped to >= 0)
  5. Build Report, fire OnHealthChecked
  6. await Task.Delay(intervalMs, ct)

StopAsync / DisposeAsync:
  1. CancellationTokenSource.Cancel()
  2. await background Task (swallow OperationCanceledException)
  3. Dispose CTS
```

### ChainIdMatch caching (EVM)

The first successful poll calls `eth_chainId` and caches the result. Subsequent
polls reuse the cached value — the chain ID of a given endpoint does not change
at runtime, so re-querying wastes RPC calls. If the first call fails (node
unreachable), retry on subsequent polls until success.

`ChainIdMatch = cachedChainId == network.ChainId`. Set to `null` when the
chain ID has never been successfully fetched yet.

## Error handling

| Condition | Behavior |
| --- | --- |
| Provider throws (network / HTTP / timeout) | `Reachable=false`, `Latency=<time spent>`, other fields `null`, `Error=ex.Message` — event still fires |
| Provider returns malformed data (e.g. negative block num, future block timestamp) | `Reachable=true`, fields populated as-is; caller's responsibility to detect anomalies |
| Event handler throws | SDK catches and logs warning (`ILogger`); polling continues |
| Cancellation | Background task exits cleanly; no final event fired |
| Repeated `StartAsync` | Second call returns immediately (no double-start) |
| `StopAsync` before `StartAsync` | No-op |
| Events after `Dispose` | Impossible (CTS cancelled, background task completed before dispose returns) |

**Principle:** The watcher reports; it does not judge. The watcher's event
handlers are treated as untrusted (handler exceptions are swallowed and
logged) — consistent with ChainKit's existing Watcher classes and the
"business errors don't throw" rule in CLAUDE.md.

## Testing strategy

**Unit tests** (NSubstitute-mocked provider, xUnit):

1. `OnHealthChecked_FiresAfterFirstPoll` — basic happy path
2. `OnHealthChecked_FiresRepeatedlyAtInterval` — periodic behavior (short interval, e.g. 50-100ms; assert N events in window)
3. `ProviderThrows_ReportShowsUnreachable` — Reachable=false, Error populated
4. `ProviderThrows_NextPollContinues` — resilience across failures
5. `StopAsync_StopsPolling` — no further events after stop
6. `EventHandlerThrows_DoesNotStopPolling` — handler exceptions swallowed
7. `DisposeAsync_CancelsGracefully` — no hang, no leaked tasks
8. `StartAsync_Twice_DoesNotDoubleStart` — idempotent
9. `ChainIdMismatch_ReportsFalse` (EVM only) — mismatch path
10. `ChainIdMatch_CachedAfterFirstSuccess` (EVM only) — verify no redundant eth_chainId calls
11. `BlockAge_ComputedCorrectly` — relative to UtcNow, clamped to non-negative
12. `StopAsync_BeforeStart_IsNoOp`

**No integration tests** in this increment — pure mocked behavior, deterministic.
Integration value is low (real node behavior is covered by existing watcher E2E).

**Coverage target:** all branches in the poll loop + lifecycle methods + error paths.

## Conventions (per CLAUDE.md)

- Both watchers are `sealed` (internal implementation, not an extension point)
- `IAsyncDisposable` matches existing `TronTransactionWatcher` / `EvmTransactionWatcher`
- Constructor accepts optional `ILogger<T>?` defaulting to `NullLogger<T>.Instance`
- XML doc comments on all public API
- `NullSigner`-style nullability discipline: nullable fields explicit, `null` means "not available"
- No `Math.Pow`, no unwrapped `JsonDocument.Parse` (not relevant in this module but stated as reminder)

## Future extensions (explicitly deferred)

- **Solidity Node probing for Tron** — add optional second endpoint probe; expose additional fields `SolidityLatency`, `SolidityReachable`, `SolidityBlockNumber` on `TronNodeHealthReport` (all nullable; null when Solidity endpoint not configured). Additive, non-breaking.
- **State-change event** — layer `OnHealthStateChanged(old, new)` on top of `OnHealthChecked` with caller-supplied predicate; can be added without modifying the existing `OnHealthChecked` contract.
- **Multi-provider selection** — a higher-level facade that takes multiple providers and returns the healthiest; out of scope here, would live one layer above Watchers.
