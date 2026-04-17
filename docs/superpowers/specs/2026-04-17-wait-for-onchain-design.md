# Wait-For-On-Chain Helpers — Design Spec

**Date**: 2026-04-17
**Status**: Approved (brainstorming)
**Scope**: Add polling-based "wait until tx is in a block" helpers to both `TronClient` and `EvmClient`.

## Motivation

ChainKit's transfer methods (`TransferTrxAsync`, EVM equivalents) return as soon as the broadcast RPC succeeds. "Broadcast success" only means the node accepted the tx into its mempool — it does **not** mean the tx is in a block. Until a block includes the tx, on-chain state (sender / receiver balances) is unchanged.

This trips up callers that chain transactions: `A → B` succeeds (broadcast OK), then immediately `B → external` fails with `balance is not sufficient` because `B`'s balance has not yet been updated on-chain. Same behavior on TronWeb today; SDK swap alone does not fix it. Callers need a primitive to bridge "broadcast accepted" → "in a block, balance updated, safe to chain next tx".

`TronTransactionWatcher` already covers this for **subscribed** addresses via `OnTrxSent` / `OnTrxReceived` events, but:

1. Event-driven flow doesn't fit linear "broadcast → wait → next tx" code paths cleanly.
2. Watcher is for long-running address monitoring; one-shot waits are awkward.
3. Existing `GetTransactionInfoByIdAsync` polls **Solidity Node** (~60s wait for solidified state), which is too conservative for "is it on-chain yet?".

A simple polling helper that hits the **Full Node**'s in-block detection (~3-6s on Tron, varies on EVM) is the right primitive. Watcher remains the right tool for ongoing monitoring; this helper is the right tool for linear flows.

## Goals

- Provide one-shot polling helpers on both `TronClient` and `EvmClient` that block until a tx is included in a block.
- Return rich tx info (block number, receipt status) so the caller can act on the result without a second RPC.
- Let the caller tune timing and failure tolerance via parameters; do not hide policy decisions inside the SDK.
- Follow .NET / C# best practices: `CancellationToken` plumbed everywhere, `OperationCanceledException` not wrapped, optional parameters with `null`-defaulted nullable structs.

## Non-Goals

- **No detection of dropped/replaced txs**. There is no reliable signal; timeout is the only safety net.
- **No exponential backoff**. Fixed interval is sufficient and avoids another knob.
- **No automatic re-broadcast**. Caller broadcasts, gets the txId, then calls this helper.
- **No "wait for N confirmations"** in this iteration. Watcher's `OnTransactionConfirmed` (Solidity Node) already covers the strongest-guarantee case for Tron; EVM confirmation depth is on `EvmTransactionWatcher`. We can revisit if real callers need it.
- **No new "lighter" Tron variant**. Tron's `gettransactioninfobyid` returns everything in one call; no benefit to a stripped-down version.

## API Surface

### `TronClient` — new method

```csharp
/// <summary>
/// Polls the Full Node until the transaction is included in a block (not solidified).
/// Use after broadcast when a follow-up tx depends on this tx's effects (e.g., spending received funds).
/// </summary>
/// <param name="txId">The transaction ID returned by the broadcast call.</param>
/// <param name="timeout">Total time to wait. Defaults to 15 seconds.</param>
/// <param name="pollInterval">Interval between polls. Defaults to 1 second.</param>
/// <param name="maxConsecutiveFailures">
/// Number of consecutive provider exceptions before giving up with ProviderConnectionFailed.
/// Set to 0 to retry indefinitely until timeout. Defaults to 5.
/// </param>
/// <param name="ct">Cancellation token. Cancellation throws OperationCanceledException (not wrapped in Result).</param>
/// <returns>
/// Ok(info) when the tx is in a block (regardless of contractRet success/failure — caller checks info.ContractResult).
/// Fail(ProviderTimeout) when deadline passes with no inclusion.
/// Fail(ProviderConnectionFailed) when the failure budget is exhausted.
/// Fail(InvalidArgument) when txId is empty/null or pollInterval is zero/negative.
/// </returns>
public Task<TronResult<TransactionInfoDto>> WaitForOnChainAsync(
    string txId,
    TimeSpan? timeout = null,
    TimeSpan? pollInterval = null,
    int maxConsecutiveFailures = 5,
    CancellationToken ct = default);
```

### `EvmClient` — two new methods

```csharp
/// <summary>
/// Polls until the transaction is mined (has a receipt). Returns the merged tx + receipt detail.
/// Use after broadcast when a follow-up tx depends on this tx's effects.
/// </summary>
/// <returns>
/// Ok(detail) when receipt is available (regardless of receipt.status — caller checks detail.Status).
/// Fail(ProviderTimeout / ProviderConnectionFailed / InvalidArgument) — see Tron variant for semantics.
/// </returns>
public Task<EvmResult<EvmTransactionDetail>> WaitForOnChainAsync(
    string txHash,
    TimeSpan? timeout = null,
    TimeSpan? pollInterval = null,
    int maxConsecutiveFailures = 5,
    CancellationToken ct = default);

/// <summary>
/// Lightweight variant: returns only the raw receipt JSON, skipping the extra eth_getTransactionByHash call.
/// Use when you only need to confirm inclusion and don't need the full merged detail.
/// </summary>
public Task<EvmResult<JsonElement>> WaitForReceiptAsync(
    string txHash,
    TimeSpan? timeout = null,
    TimeSpan? pollInterval = null,
    int maxConsecutiveFailures = 5,
    CancellationToken ct = default);
```

### `TronHttpProvider` — modified method

`GetTransactionInfoByIdAsync` gains a `useSolidity` parameter (default `true` to preserve current behavior — non-breaking):

```csharp
/// <summary>
/// Gets transaction info by id.
/// </summary>
/// <param name="txId">Transaction ID.</param>
/// <param name="useSolidity">
/// When true (default), reads from /walletsolidity/gettransactioninfobyid (only solidified txs visible).
/// When false, reads from /wallet/gettransactioninfobyid (in-block txs visible, ~3-6s after broadcast).
/// </param>
public Task<TransactionInfoDto> GetTransactionInfoByIdAsync(
    string txId,
    bool useSolidity = true,
    CancellationToken ct = default);
```

`ITronProvider` interface gets the same signature update.

### `EvmHttpProvider` — no changes needed

`GetTransactionReceiptAsync` and `GetTransactionByHashAsync` already exist and return what we need.

### Defaults — declared as constants, not magic literals

```csharp
// TronClient
private static readonly TimeSpan DefaultWaitOnChainTimeout = TimeSpan.FromSeconds(15);
private static readonly TimeSpan DefaultWaitOnChainPollInterval = TimeSpan.FromSeconds(1);

// EvmClient
private static readonly TimeSpan DefaultWaitOnChainTimeout = TimeSpan.FromSeconds(60);
private static readonly TimeSpan DefaultWaitOnChainPollInterval = TimeSpan.FromSeconds(2);
```

Rationale: Tron block time is ~3s, so 15s ≈ 5 attempts is plenty. EVM varies (Polygon ~2s, Ethereum ~12s); 60s with 2s interval gives ~5 Ethereum blocks of headroom.

## Implementation

### Polling loop (shared shape, both chains)

Use `do-while` so the helper always polls at least once, even if `timeout == TimeSpan.Zero`. This makes "wait, but I don't really want to wait" usable as a single fast-path probe.

```
deadline = UtcNow + timeout
failures = 0

do:
    ct.ThrowIfCancellationRequested()
    try:
        result = await ProviderQuery(...)         // (Tron) GetTransactionInfoByIdAsync(useSolidity: false)
                                                  // (EVM) GetTransactionReceiptAsync
        failures = 0                              // reset on any successful RPC, even if not yet on-chain
        if result indicates "on chain":
            return Ok(buildReturnValue(result))
    catch Exception ex when not OperationCanceledException:
        failures++
        if maxConsecutiveFailures > 0 and failures >= maxConsecutiveFailures:
            return Fail(ProviderConnectionFailed, ex.Message)
        _logger.LogWarning(ex, "WaitForOnChain: provider call failed (attempt {Failures})", failures)
    if UtcNow >= deadline: break
    await Task.Delay(pollInterval, ct)
while true

return Fail(ProviderTimeout, ...)
```

### "On chain" detection

- **Tron**: `info.BlockNumber > 0`. The Full Node `/wallet/gettransactioninfobyid` returns `{}` (parsed to `BlockNumber == 0`) while the tx is in mempool; once executed in a block, the response includes `blockNumber`, `blockTimeStamp`, `receipt`, etc.
- **EVM**: `receipt is not null`. `eth_getTransactionReceipt` returns `null` until mined.

### EVM `WaitForOnChainAsync` — building `EvmTransactionDetail`

Polling itself is one RPC per iteration (`eth_getTransactionReceipt`). When the receipt appears, fire one additional `eth_getTransactionByHash` to merge into `EvmTransactionDetail` via the existing `BuildTransactionDetail` helper. Total: N polls + 1 final RPC. Cost is negligible.

`WaitForReceiptAsync` skips that extra call and returns the raw receipt only.

### Argument validation (no RPC if invalid)

- `txId`/`txHash` null or empty → `Fail(InvalidArgument)`, no RPC fired
- `pollInterval <= TimeSpan.Zero` → `Fail(InvalidArgument)` (would burn CPU)
- `timeout < TimeSpan.Zero` → `Fail(InvalidArgument)`
- `timeout == TimeSpan.Zero` → legal, polls exactly once (via do-while) then times out
- `maxConsecutiveFailures < 0` → `Fail(InvalidArgument)`

### Error codes — additions required

Verified against current source:
- `TronErrorCode` (`src/ChainKit.Tron/Models/TronErrorCode.cs`) already has `ProviderTimeout` and `ProviderConnectionFailed`. **Add: `InvalidArgument`**.
- `EvmErrorCode` (`src/ChainKit.Evm/Models/EvmErrorCode.cs`) already has `ProviderTimeout` and `ProviderConnectionFailed`. **Add: `InvalidArgument`**.

Both additions are non-breaking (appended to enum).

## Error & Edge Case Matrix

| Situation | Return |
|---|---|
| Tx in block, contractRet=SUCCESS / status=0x1 | `Ok(info|detail)` |
| Tx in block, contractRet=FAILED / status=0x0 (revert) | **`Ok(info|detail)`** — caller inspects status |
| Deadline passes, never seen | `Fail(ProviderTimeout)` |
| `maxConsecutiveFailures` reached | `Fail(ProviderConnectionFailed)` |
| `ct` triggered | `throw OperationCanceledException` (not wrapped) |
| Bad arguments | `Fail(InvalidArgument)`, no RPC |

Rationale for "Ok on revert": the SDK's job is "did the wait complete". Whether the tx semantically did what the caller wanted is a business concern; the caller has the full `info` / `detail` to inspect.

## Testing Strategy

### Unit tests (Tron)

- Mock `ITronProvider.GetTransactionInfoByIdAsync(_, useSolidity: false, _)`:
  - Returns `BlockNumber == 0` N times then `BlockNumber > 0` → assert `Ok`, assert call count == N+1
  - Always returns `BlockNumber == 0` → assert `Fail(ProviderTimeout)`
  - Throws N consecutive times (N == maxConsecutiveFailures) → assert `Fail(ProviderConnectionFailed)`
  - Throws (N-1) times then succeeds → assert counter resets, assert `Ok`
  - `maxConsecutiveFailures = 0` + always throws → assert keeps retrying until timeout (no early give-up)
- Cancellation mid-wait → assert `OperationCanceledException` propagates
- Bad args (null txId, zero pollInterval, negative timeout, negative maxConsecutiveFailures) → assert `Fail(InvalidArgument)` + no provider call

### Unit tests (`TronHttpProvider`)

- `GetTransactionInfoByIdAsync(useSolidity: false)` → assert HTTP request path is `/wallet/gettransactioninfobyid`
- `GetTransactionInfoByIdAsync(useSolidity: true)` (default) → assert path is `/walletsolidity/gettransactioninfobyid` (current behavior unchanged)

### Unit tests (EVM)

Mirror the Tron suite for `WaitForOnChainAsync` and `WaitForReceiptAsync`:
- `WaitForOnChainAsync` happy path → assert receipt + `eth_getTransactionByHash` both called, `EvmTransactionDetail` populated
- `WaitForReceiptAsync` happy path → assert ONLY `eth_getTransactionReceipt` called (no extra tx-data call)
- All Tron-style timeout / failure / cancellation / arg-validation cases

### Integration tests (optional, gated by `Category=Integration`/`E2E`)

- Tron Nile: broadcast a real transfer, `WaitForOnChainAsync`, assert succeeds within 10 seconds
- EVM Anvil: same flow, assert succeeds quickly (Anvil mines instantly, so should be ~1 poll)

## Files Changed

- `src/ChainKit.Tron/Providers/ITronProvider.cs` — add `useSolidity` param to `GetTransactionInfoByIdAsync`
- `src/ChainKit.Tron/Providers/TronHttpProvider.cs` — implement new param, route to Full Node when false
- `src/ChainKit.Tron/Providers/TronGrpcProvider.cs` — same interface update (gRPC has separate full vs solidity stubs)
- `src/ChainKit.Tron/TronClient.cs` — add `WaitForOnChainAsync`
- `src/ChainKit.Tron/Models/TronErrorCode.cs` — add `InvalidArgument`
- `src/ChainKit.Evm/EvmClient.cs` — add `WaitForOnChainAsync`, `WaitForReceiptAsync`
- `src/ChainKit.Evm/Models/EvmErrorCode.cs` — add `InvalidArgument`
- `tests/ChainKit.Tron.Tests/...` — unit tests above
- `tests/ChainKit.Evm.Tests/...` — unit tests above
- `docs/tron-sdk-usage-guide.md` — add usage example for chained transfers
- `docs/evm-sdk-usage-guide.md` — same
- `CLAUDE.md` — add convention note about wait-for-on-chain helper

## Open Questions

None. All resolved during brainstorming:
- **Scope**: Both chains.
- **Return type**: Rich (`TransactionInfoDto` / `EvmTransactionDetail`); EVM also gets a lightweight `WaitForReceiptAsync`.
- **Naming**: `WaitForOnChainAsync`.
- **Defaults**: 15s/1s (Tron), 60s/2s (EVM).
- **Revert handling**: Return `Ok` with full info; caller checks status.
- **Cancellation**: Propagate `OperationCanceledException`.
- **Failure tolerance**: Caller-configurable via `maxConsecutiveFailures` (default 5, 0 = unlimited).
