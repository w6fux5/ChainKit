# Wait-For-On-Chain Helpers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add polling-based "wait until tx is in a block" helpers to `TronClient` and `EvmClient` so callers can safely chain transactions (e.g., `A→B` then immediately `B→external` without `balance is not sufficient`).

**Architecture:** A `WaitForOnChainAsync` method on each client polls the chain (Full Node `/wallet/gettransactioninfobyid` for Tron, `eth_getTransactionReceipt` for EVM) at a caller-tunable interval until the tx is in a block, then returns rich info (block number, receipt status). EVM also gets a lighter `WaitForReceiptAsync` that skips the extra `eth_getTransactionByHash` call.

**Tech Stack:** .NET 10, C#, xUnit, NSubstitute. Spec at `docs/superpowers/specs/2026-04-17-wait-for-onchain-design.md`.

**Branch:** `feature/wait-for-onchain` (already checked out, spec already committed).

---

## File Structure

Files this plan creates / modifies:

| File | Responsibility |
|---|---|
| `src/ChainKit.Tron/Models/TronErrorCode.cs` | Add `InvalidArgument` enum value |
| `src/ChainKit.Evm/Models/EvmErrorCode.cs` | Add `InvalidArgument` enum value |
| `src/ChainKit.Tron/Providers/ITronProvider.cs` | Add `useSolidity` param to `GetTransactionInfoByIdAsync` |
| `src/ChainKit.Tron/Providers/TronHttpProvider.cs` | Implement `useSolidity` switch (Full Node when false) |
| `src/ChainKit.Tron/Providers/TronGrpcProvider.cs` | Implement `useSolidity` switch (use `_fullNodeInvoker` when false) |
| `src/ChainKit.Tron/TronClient.cs` | Add `WaitForOnChainAsync` |
| `src/ChainKit.Evm/EvmClient.cs` | Add `WaitForOnChainAsync` and `WaitForReceiptAsync` |
| `tests/ChainKit.Tron.Tests/Providers/TronHttpProviderTests.cs` | Tests for `useSolidity` routing (create if missing, append if exists) |
| `tests/ChainKit.Tron.Tests/TronClientTests.cs` | Append tests for `WaitForOnChainAsync` |
| `tests/ChainKit.Evm.Tests/EvmClientTests.cs` | Append tests for both EVM wait methods |
| `docs/tron-sdk-usage-guide.md` | Add chained transfer usage example |
| `docs/evm-sdk-usage-guide.md` | Add chained transfer usage example |
| `CLAUDE.md` | Add convention note |

---

## Task 1: Add `InvalidArgument` to both error code enums

**Files:**
- Modify: `src/ChainKit.Tron/Models/TronErrorCode.cs`
- Modify: `src/ChainKit.Evm/Models/EvmErrorCode.cs`

This is non-test foundation work — single trivial enum addition each side. No unit tests needed (enum values don't have behavior to test in isolation).

- [ ] **Step 1: Add `InvalidArgument` to `TronErrorCode`**

Edit `src/ChainKit.Tron/Models/TronErrorCode.cs`. After the existing `ProviderTimeout` line, add `InvalidArgument`:

```csharp
namespace ChainKit.Tron.Models;

public enum TronErrorCode
{
    Unknown,
    InvalidAddress,
    InvalidAmount,
    InsufficientBalance,
    InsufficientEnergy,
    InsufficientBandwidth,
    ContractExecutionFailed,
    ContractValidationFailed,
    TransactionExpired,
    DuplicateTransaction,
    ProviderConnectionFailed,
    ProviderTimeout,
    InvalidArgument
}
```

- [ ] **Step 2: Add `InvalidArgument` to `EvmErrorCode`**

Edit `src/ChainKit.Evm/Models/EvmErrorCode.cs`:

```csharp
namespace ChainKit.Evm.Models;

public enum EvmErrorCode
{
    Unknown,
    InvalidAddress,
    InvalidAmount,
    InsufficientBalance,
    InsufficientGasBalance,
    NonceTooLow,
    NonceTooHigh,
    GasPriceTooLow,
    GasLimitExceeded,
    ContractReverted,
    ContractNotFound,
    TransactionNotFound,
    ProviderConnectionFailed,
    ProviderTimeout,
    ProviderRpcError,
    InvalidArgument
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build`
Expected: Build succeeds with no errors (no callers exist yet for the new value).

- [ ] **Step 4: Commit**

```bash
git add src/ChainKit.Tron/Models/TronErrorCode.cs src/ChainKit.Evm/Models/EvmErrorCode.cs
git commit -m "$(cat <<'EOF'
feat: add InvalidArgument to TronErrorCode and EvmErrorCode

Used by upcoming WaitForOnChainAsync helpers for argument validation
failures (null txId, zero pollInterval, negative timeout, etc.).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Add `useSolidity` parameter to `GetTransactionInfoByIdAsync`

**Files:**
- Modify: `src/ChainKit.Tron/Providers/ITronProvider.cs:14`
- Modify: `src/ChainKit.Tron/Providers/TronHttpProvider.cs:176-180`
- Modify: `src/ChainKit.Tron/Providers/TronGrpcProvider.cs:273-284`
- Create or append: `tests/ChainKit.Tron.Tests/Providers/TronHttpProviderTests.cs`

Default value `useSolidity: true` preserves existing behavior — `TronTransactionWatcher` (which calls this method) is unchanged.

- [ ] **Step 1: Write failing test for `useSolidity: false` routing**

First check if `tests/ChainKit.Tron.Tests/Providers/TronHttpProviderTests.cs` exists (likely does — directory listed). If yes, append. If no, create with namespace and class scaffolding.

Test class needs a custom `HttpMessageHandler` to capture request URLs. If a similar pattern already exists in `TronHttpProviderTests.cs`, reuse it; otherwise add this helper at the bottom of the test class:

```csharp
private sealed class CapturingHandler : HttpMessageHandler
{
    public List<string> RequestUrls { get; } = new();
    public string ResponseJson { get; set; } = "{}";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUrls.Add(request.RequestUri!.ToString());
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(ResponseJson)
        });
    }
}
```

Then add the test:

```csharp
[Fact]
public async Task GetTransactionInfoByIdAsync_UseSolidityFalse_HitsFullNodeEndpoint()
{
    var handler = new CapturingHandler();
    using var http = new HttpClient(handler);
    using var provider = new TronHttpProvider(http,
        baseUrl: "http://full:8090",
        solidityUrl: "http://solidity:8091");

    await provider.GetTransactionInfoByIdAsync("abc123", useSolidity: false);

    Assert.Single(handler.RequestUrls);
    Assert.Equal("http://full:8090/wallet/gettransactioninfobyid", handler.RequestUrls[0]);
}

[Fact]
public async Task GetTransactionInfoByIdAsync_UseSolidityTrue_HitsSolidityEndpoint()
{
    var handler = new CapturingHandler();
    using var http = new HttpClient(handler);
    using var provider = new TronHttpProvider(http,
        baseUrl: "http://full:8090",
        solidityUrl: "http://solidity:8091");

    await provider.GetTransactionInfoByIdAsync("abc123", useSolidity: true);

    Assert.Single(handler.RequestUrls);
    Assert.Equal("http://solidity:8091/walletsolidity/gettransactioninfobyid", handler.RequestUrls[0]);
}

[Fact]
public async Task GetTransactionInfoByIdAsync_DefaultUsesSolidity()
{
    var handler = new CapturingHandler();
    using var http = new HttpClient(handler);
    using var provider = new TronHttpProvider(http,
        baseUrl: "http://full:8090",
        solidityUrl: "http://solidity:8091");

    await provider.GetTransactionInfoByIdAsync("abc123");

    Assert.Equal("http://solidity:8091/walletsolidity/gettransactioninfobyid", handler.RequestUrls[0]);
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/ChainKit.Tron.Tests --filter "FullyQualifiedName~GetTransactionInfoByIdAsync_UseSolidity"`
Expected: Compile error (the `useSolidity` parameter doesn't exist on the method yet).

- [ ] **Step 3: Update `ITronProvider` interface**

Edit `src/ChainKit.Tron/Providers/ITronProvider.cs` line 14:

```csharp
Task<TransactionInfoDto> GetTransactionInfoByIdAsync(string txId, bool useSolidity = true, CancellationToken ct = default);
```

- [ ] **Step 4: Update `TronHttpProvider` implementation**

Edit `src/ChainKit.Tron/Providers/TronHttpProvider.cs` lines 176-180. Replace:

```csharp
public async Task<TransactionInfoDto> GetTransactionInfoByIdAsync(string txId, CancellationToken ct = default)
{
    var json = await PostSolidityAsync("/walletsolidity/gettransactioninfobyid", new { value = txId }, ct);
    return ParseTransactionInfo(json, txId);
}
```

with:

```csharp
public async Task<TransactionInfoDto> GetTransactionInfoByIdAsync(string txId, bool useSolidity = true, CancellationToken ct = default)
{
    var json = useSolidity
        ? await PostSolidityAsync("/walletsolidity/gettransactioninfobyid", new { value = txId }, ct)
        : await PostAsync("/wallet/gettransactioninfobyid", new { value = txId }, ct);
    return ParseTransactionInfo(json, txId);
}
```

- [ ] **Step 5: Update `TronGrpcProvider` implementation**

Edit `src/ChainKit.Tron/Providers/TronGrpcProvider.cs` lines 273-284. Replace:

```csharp
public async Task<TransactionInfoDto> GetTransactionInfoByIdAsync(string txId, CancellationToken ct = default)
{
    // BytesMessage: field 1 (bytes) = value (the tx hash)
    var txHash = Convert.FromHexString(txId);
    var request = EncodeField(1, txHash);

    var invoker = _solidityInvoker ?? _fullNodeInvoker;
    var response = await CallAsync(invoker, GetTransactionInfoByIdMethod, request, ct);

    // Parse TransactionInfo response
    return ParseTransactionInfo(response, txId);
}
```

with:

```csharp
public async Task<TransactionInfoDto> GetTransactionInfoByIdAsync(string txId, bool useSolidity = true, CancellationToken ct = default)
{
    var txHash = Convert.FromHexString(txId);
    var request = EncodeField(1, txHash);

    var invoker = useSolidity
        ? (_solidityInvoker ?? _fullNodeInvoker)
        : _fullNodeInvoker;
    var response = await CallAsync(invoker, GetTransactionInfoByIdMethod, request, ct);

    return ParseTransactionInfo(response, txId);
}
```

- [ ] **Step 6: Run tests to verify pass**

Run: `dotnet test tests/ChainKit.Tron.Tests --filter "FullyQualifiedName~GetTransactionInfoByIdAsync_UseSolidity OR FullyQualifiedName~GetTransactionInfoByIdAsync_Default"`
Expected: All 3 tests pass.

- [ ] **Step 7: Run full test suite to verify nothing broke**

Run: `dotnet test --filter "Category!=Integration&Category!=E2E"`
Expected: All tests pass. Pay attention to any `TronTransactionWatcher` tests — they should still work because the parameter has a default value.

- [ ] **Step 8: Commit**

```bash
git add src/ChainKit.Tron/Providers/ITronProvider.cs \
        src/ChainKit.Tron/Providers/TronHttpProvider.cs \
        src/ChainKit.Tron/Providers/TronGrpcProvider.cs \
        tests/ChainKit.Tron.Tests/Providers/TronHttpProviderTests.cs
git commit -m "$(cat <<'EOF'
feat(tron): add useSolidity param to GetTransactionInfoByIdAsync

Default useSolidity=true preserves existing TronTransactionWatcher behavior.
useSolidity=false routes to Full Node /wallet/gettransactioninfobyid for
faster in-block detection (~3-6s vs ~60s for solidified). Used by upcoming
TronClient.WaitForOnChainAsync.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Implement `TronClient.WaitForOnChainAsync`

**Files:**
- Modify: `src/ChainKit.Tron/TronClient.cs` (append new method, add private constants near top of class)
- Modify: `tests/ChainKit.Tron.Tests/TronClientTests.cs` (append tests)

- [ ] **Step 1: Write failing test — happy path**

Append to `tests/ChainKit.Tron.Tests/TronClientTests.cs`. Use `Substitute.For<ITronProvider>()` (already in fixture as `_provider`):

```csharp
// === WaitForOnChainAsync ===

private static TransactionInfoDto EmptyTxInfo(string txId) =>
    new(TxId: txId, BlockNumber: 0, BlockTimestamp: 0, ContractResult: "",
        Fee: 0, EnergyUsage: 0, NetUsage: 0);

private static TransactionInfoDto OnChainTxInfo(string txId, long blockNumber = 12345) =>
    new(TxId: txId, BlockNumber: blockNumber, BlockTimestamp: 1700000000000,
        ContractResult: "SUCCESS", Fee: 1000, EnergyUsage: 0, NetUsage: 268);

[Fact]
public async Task WaitForOnChainAsync_TxAppearsAfterTwoPolls_ReturnsOk()
{
    var calls = 0;
    _provider.GetTransactionInfoByIdAsync(
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
        .Returns(_ =>
        {
            calls++;
            return Task.FromResult(calls < 3 ? EmptyTxInfo("txA") : OnChainTxInfo("txA"));
        });

    var result = await _client.WaitForOnChainAsync("txA",
        timeout: TimeSpan.FromSeconds(5),
        pollInterval: TimeSpan.FromMilliseconds(10));

    Assert.True(result.Success);
    Assert.NotNull(result.Data);
    Assert.Equal(12345L, result.Data!.BlockNumber);
    Assert.Equal(3, calls);
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/ChainKit.Tron.Tests --filter "FullyQualifiedName~WaitForOnChainAsync_TxAppearsAfterTwoPolls"`
Expected: Compile error — `WaitForOnChainAsync` method does not exist.

- [ ] **Step 3: Add constants and method to `TronClient`**

Edit `src/ChainKit.Tron/TronClient.cs`. Near the top of the class (after the existing private fields), add constants:

```csharp
private static readonly TimeSpan DefaultWaitOnChainTimeout = TimeSpan.FromSeconds(15);
private static readonly TimeSpan DefaultWaitOnChainPollInterval = TimeSpan.FromSeconds(1);
```

At the end of the class (before the closing brace), add the method:

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
public async Task<TronResult<TransactionInfoDto>> WaitForOnChainAsync(
    string txId,
    TimeSpan? timeout = null,
    TimeSpan? pollInterval = null,
    int maxConsecutiveFailures = 5,
    CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(txId))
        return TronResult<TransactionInfoDto>.Fail(TronErrorCode.InvalidArgument, "txId must not be null or empty");
    if (maxConsecutiveFailures < 0)
        return TronResult<TransactionInfoDto>.Fail(TronErrorCode.InvalidArgument, "maxConsecutiveFailures must be >= 0");

    var effectiveTimeout = timeout ?? DefaultWaitOnChainTimeout;
    if (effectiveTimeout < TimeSpan.Zero)
        return TronResult<TransactionInfoDto>.Fail(TronErrorCode.InvalidArgument, "timeout must be >= zero");

    var effectivePollInterval = pollInterval ?? DefaultWaitOnChainPollInterval;
    if (effectivePollInterval <= TimeSpan.Zero)
        return TronResult<TransactionInfoDto>.Fail(TronErrorCode.InvalidArgument, "pollInterval must be > zero");

    var deadline = DateTime.UtcNow + effectiveTimeout;
    var failures = 0;
    string? lastFailureMessage = null;

    while (true)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var info = await Provider.GetTransactionInfoByIdAsync(txId, useSolidity: false, ct);
            failures = 0;
            if (info.BlockNumber > 0)
                return TronResult<TransactionInfoDto>.Ok(info);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            failures++;
            lastFailureMessage = ex.Message;
            _logger.LogWarning(ex, "WaitForOnChainAsync: provider call failed (attempt {Failures})", failures);
            if (maxConsecutiveFailures > 0 && failures >= maxConsecutiveFailures)
                return TronResult<TransactionInfoDto>.Fail(TronErrorCode.ProviderConnectionFailed, ex.Message);
        }

        if (DateTime.UtcNow >= deadline)
        {
            var msg = lastFailureMessage is null
                ? $"Transaction {txId} not on-chain within {effectiveTimeout}"
                : $"Transaction {txId} not on-chain within {effectiveTimeout} (last error: {lastFailureMessage})";
            return TronResult<TransactionInfoDto>.Fail(TronErrorCode.ProviderTimeout, msg);
        }

        await Task.Delay(effectivePollInterval, ct);
    }
}
```

Note: the `_logger` field already exists on `TronClient` (used by other methods such as `TransferTrxAsync`). If you can't find it, search `TronClient.cs` for `_logger.LogError` to confirm — it's there.

- [ ] **Step 4: Run happy-path test to verify pass**

Run: `dotnet test tests/ChainKit.Tron.Tests --filter "FullyQualifiedName~WaitForOnChainAsync_TxAppearsAfterTwoPolls"`
Expected: PASS.

- [ ] **Step 5: Add timeout test**

Append to test class:

```csharp
[Fact]
public async Task WaitForOnChainAsync_NeverOnChain_TimesOut()
{
    _provider.GetTransactionInfoByIdAsync(
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
        .Returns(EmptyTxInfo("txT"));

    var result = await _client.WaitForOnChainAsync("txT",
        timeout: TimeSpan.FromMilliseconds(50),
        pollInterval: TimeSpan.FromMilliseconds(10));

    Assert.False(result.Success);
    Assert.Equal(TronErrorCode.ProviderTimeout, result.ErrorCode);
}
```

Run: `dotnet test tests/ChainKit.Tron.Tests --filter "FullyQualifiedName~WaitForOnChainAsync_NeverOnChain"`
Expected: PASS (already implemented).

- [ ] **Step 6: Add failure tolerance tests**

Append:

```csharp
[Fact]
public async Task WaitForOnChainAsync_ConsecutiveFailures_ReturnsProviderConnectionFailed()
{
    _provider.GetTransactionInfoByIdAsync(
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
        .ThrowsAsync(new HttpRequestException("network down"));

    var result = await _client.WaitForOnChainAsync("txF",
        timeout: TimeSpan.FromSeconds(10),
        pollInterval: TimeSpan.FromMilliseconds(10),
        maxConsecutiveFailures: 3);

    Assert.False(result.Success);
    Assert.Equal(TronErrorCode.ProviderConnectionFailed, result.ErrorCode);
}

[Fact]
public async Task WaitForOnChainAsync_FailureThenSuccess_ResetsCounter()
{
    var calls = 0;
    _provider.GetTransactionInfoByIdAsync(
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
        .Returns(_ =>
        {
            calls++;
            if (calls <= 2) throw new HttpRequestException("flaky");
            if (calls == 3) return Task.FromResult(EmptyTxInfo("txR"));
            return Task.FromResult(OnChainTxInfo("txR"));
        });

    var result = await _client.WaitForOnChainAsync("txR",
        timeout: TimeSpan.FromSeconds(5),
        pollInterval: TimeSpan.FromMilliseconds(10),
        maxConsecutiveFailures: 3);

    // 2 throws then success — counter resets, never hits the threshold.
    Assert.True(result.Success);
    Assert.Equal(4, calls);
}

[Fact]
public async Task WaitForOnChainAsync_MaxFailuresZero_RetriesUntilTimeout()
{
    _provider.GetTransactionInfoByIdAsync(
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
        .ThrowsAsync(new HttpRequestException("always down"));

    var result = await _client.WaitForOnChainAsync("txU",
        timeout: TimeSpan.FromMilliseconds(50),
        pollInterval: TimeSpan.FromMilliseconds(10),
        maxConsecutiveFailures: 0);

    Assert.False(result.Success);
    Assert.Equal(TronErrorCode.ProviderTimeout, result.ErrorCode);
}
```

Run: `dotnet test tests/ChainKit.Tron.Tests --filter "FullyQualifiedName~WaitForOnChainAsync_ConsecutiveFailures OR FullyQualifiedName~WaitForOnChainAsync_FailureThenSuccess OR FullyQualifiedName~WaitForOnChainAsync_MaxFailuresZero"`
Expected: All 3 PASS.

- [ ] **Step 7: Add cancellation test**

Append:

```csharp
[Fact]
public async Task WaitForOnChainAsync_Cancelled_ThrowsOperationCanceled()
{
    _provider.GetTransactionInfoByIdAsync(
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
        .Returns(EmptyTxInfo("txC"));

    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        _client.WaitForOnChainAsync("txC",
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(5),
            ct: cts.Token));
}
```

Run: `dotnet test tests/ChainKit.Tron.Tests --filter "FullyQualifiedName~WaitForOnChainAsync_Cancelled"`
Expected: PASS.

- [ ] **Step 8: Add argument validation tests**

Append:

```csharp
[Theory]
[InlineData(null)]
[InlineData("")]
[InlineData("   ")]
public async Task WaitForOnChainAsync_BadTxId_ReturnsInvalidArgument(string? txId)
{
    var result = await _client.WaitForOnChainAsync(txId!);

    Assert.False(result.Success);
    Assert.Equal(TronErrorCode.InvalidArgument, result.ErrorCode);
    await _provider.DidNotReceive().GetTransactionInfoByIdAsync(
        Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task WaitForOnChainAsync_ZeroPollInterval_ReturnsInvalidArgument()
{
    var result = await _client.WaitForOnChainAsync("txZ",
        pollInterval: TimeSpan.Zero);

    Assert.False(result.Success);
    Assert.Equal(TronErrorCode.InvalidArgument, result.ErrorCode);
}

[Fact]
public async Task WaitForOnChainAsync_NegativeTimeout_ReturnsInvalidArgument()
{
    var result = await _client.WaitForOnChainAsync("txN",
        timeout: TimeSpan.FromSeconds(-1));

    Assert.False(result.Success);
    Assert.Equal(TronErrorCode.InvalidArgument, result.ErrorCode);
}

[Fact]
public async Task WaitForOnChainAsync_NegativeMaxFailures_ReturnsInvalidArgument()
{
    var result = await _client.WaitForOnChainAsync("txM",
        maxConsecutiveFailures: -1);

    Assert.False(result.Success);
    Assert.Equal(TronErrorCode.InvalidArgument, result.ErrorCode);
}
```

Run: `dotnet test tests/ChainKit.Tron.Tests --filter "FullyQualifiedName~WaitForOnChainAsync_BadTxId OR FullyQualifiedName~WaitForOnChainAsync_ZeroPollInterval OR FullyQualifiedName~WaitForOnChainAsync_NegativeTimeout OR FullyQualifiedName~WaitForOnChainAsync_NegativeMaxFailures"`
Expected: All PASS.

- [ ] **Step 9: Add `timeout = Zero` polls-once test**

Append:

```csharp
[Fact]
public async Task WaitForOnChainAsync_TimeoutZero_PollsOnceThenTimesOut()
{
    var calls = 0;
    _provider.GetTransactionInfoByIdAsync(
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
        .Returns(_ => { calls++; return Task.FromResult(EmptyTxInfo("txZ0")); });

    var result = await _client.WaitForOnChainAsync("txZ0",
        timeout: TimeSpan.Zero,
        pollInterval: TimeSpan.FromMilliseconds(10));

    Assert.False(result.Success);
    Assert.Equal(TronErrorCode.ProviderTimeout, result.ErrorCode);
    Assert.Equal(1, calls);
}
```

Run: `dotnet test tests/ChainKit.Tron.Tests --filter "FullyQualifiedName~WaitForOnChainAsync_TimeoutZero"`
Expected: PASS.

- [ ] **Step 10: Run all `TronClient` tests for regression**

Run: `dotnet test tests/ChainKit.Tron.Tests --filter "FullyQualifiedName~TronClientTests"`
Expected: All tests pass.

- [ ] **Step 11: Commit**

```bash
git add src/ChainKit.Tron/TronClient.cs tests/ChainKit.Tron.Tests/TronClientTests.cs
git commit -m "$(cat <<'EOF'
feat(tron): add TronClient.WaitForOnChainAsync

Polls Full Node gettransactioninfobyid until the tx is included in a
block (~3-6s on Tron). Caller-tunable timeout, pollInterval, and
maxConsecutiveFailures. Returns rich TransactionInfoDto so caller can
inspect contractRet without an extra RPC. Cancellation propagates
OperationCanceledException per .NET conventions.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Implement `EvmClient.WaitForReceiptAsync`

**Files:**
- Modify: `src/ChainKit.Evm/EvmClient.cs` (append new method + constants)
- Modify: `tests/ChainKit.Evm.Tests/EvmClientTests.cs` (append tests)

Lighter-weight: returns the raw receipt JSON, no extra `eth_getTransactionByHash` call.

- [ ] **Step 1: Write failing test — happy path**

Append to `tests/ChainKit.Evm.Tests/EvmClientTests.cs`:

```csharp
// === WaitForReceiptAsync ===

private static JsonElement BuildReceipt(string status = "0x1", string blockNumber = "0x10")
{
    var json = $"{{\"status\":\"{status}\",\"blockNumber\":\"{blockNumber}\",\"gasUsed\":\"0x5208\",\"effectiveGasPrice\":\"0x1\"}}";
    using var doc = JsonDocument.Parse(json);
    return doc.RootElement.Clone();
}

[Fact]
public async Task WaitForReceiptAsync_ReceiptAppearsAfterTwoPolls_ReturnsOk()
{
    var calls = 0;
    _provider.GetTransactionReceiptAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
        .Returns(_ =>
        {
            calls++;
            return Task.FromResult<JsonElement?>(calls < 3 ? null : BuildReceipt());
        });

    var result = await _client.WaitForReceiptAsync("0xabc",
        timeout: TimeSpan.FromSeconds(5),
        pollInterval: TimeSpan.FromMilliseconds(10));

    Assert.True(result.Success);
    Assert.Equal("0x1", result.Data.GetProperty("status").GetString());
    Assert.Equal(3, calls);
    await _provider.DidNotReceive().GetTransactionByHashAsync(
        Arg.Any<string>(), Arg.Any<CancellationToken>());
}
```

Run: `dotnet test tests/ChainKit.Evm.Tests --filter "FullyQualifiedName~WaitForReceiptAsync_ReceiptAppearsAfterTwoPolls"`
Expected: Compile error (method does not exist).

- [ ] **Step 2: Add constants and method to `EvmClient`**

Edit `src/ChainKit.Evm/EvmClient.cs`. Near the top of the class (after existing private fields), add:

```csharp
private static readonly TimeSpan DefaultWaitOnChainTimeout = TimeSpan.FromSeconds(60);
private static readonly TimeSpan DefaultWaitOnChainPollInterval = TimeSpan.FromSeconds(2);
```

Append to the class (before closing brace):

```csharp
/// <summary>
/// Polls until the transaction has a receipt (mined into a block).
/// Lightweight variant: returns only the raw receipt JSON, skipping eth_getTransactionByHash.
/// Use when you only need to confirm inclusion and don't need the full merged detail.
/// </summary>
/// <param name="txHash">The transaction hash returned by the broadcast call.</param>
/// <param name="timeout">Total time to wait. Defaults to 60 seconds.</param>
/// <param name="pollInterval">Interval between polls. Defaults to 2 seconds.</param>
/// <param name="maxConsecutiveFailures">
/// Number of consecutive provider exceptions before giving up. Set to 0 to retry indefinitely
/// until timeout. Defaults to 5.
/// </param>
/// <param name="ct">Cancellation token. Cancellation throws OperationCanceledException.</param>
public async Task<EvmResult<JsonElement>> WaitForReceiptAsync(
    string txHash,
    TimeSpan? timeout = null,
    TimeSpan? pollInterval = null,
    int maxConsecutiveFailures = 5,
    CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(txHash))
        return EvmResult<JsonElement>.Fail(EvmErrorCode.InvalidArgument, "txHash must not be null or empty");
    if (maxConsecutiveFailures < 0)
        return EvmResult<JsonElement>.Fail(EvmErrorCode.InvalidArgument, "maxConsecutiveFailures must be >= 0");

    var effectiveTimeout = timeout ?? DefaultWaitOnChainTimeout;
    if (effectiveTimeout < TimeSpan.Zero)
        return EvmResult<JsonElement>.Fail(EvmErrorCode.InvalidArgument, "timeout must be >= zero");

    var effectivePollInterval = pollInterval ?? DefaultWaitOnChainPollInterval;
    if (effectivePollInterval <= TimeSpan.Zero)
        return EvmResult<JsonElement>.Fail(EvmErrorCode.InvalidArgument, "pollInterval must be > zero");

    var deadline = DateTime.UtcNow + effectiveTimeout;
    var failures = 0;
    string? lastFailureMessage = null;

    while (true)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var receipt = await Provider.GetTransactionReceiptAsync(txHash, ct);
            failures = 0;
            if (receipt is not null)
                return EvmResult<JsonElement>.Ok(receipt.Value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            failures++;
            lastFailureMessage = ex.Message;
            _logger.LogWarning(ex, "WaitForReceiptAsync: provider call failed (attempt {Failures})", failures);
            if (maxConsecutiveFailures > 0 && failures >= maxConsecutiveFailures)
                return EvmResult<JsonElement>.Fail(EvmErrorCode.ProviderConnectionFailed, ex.Message);
        }

        if (DateTime.UtcNow >= deadline)
        {
            var msg = lastFailureMessage is null
                ? $"Transaction {txHash} has no receipt within {effectiveTimeout}"
                : $"Transaction {txHash} has no receipt within {effectiveTimeout} (last error: {lastFailureMessage})";
            return EvmResult<JsonElement>.Fail(EvmErrorCode.ProviderTimeout, msg);
        }

        await Task.Delay(effectivePollInterval, ct);
    }
}
```

Run: `dotnet test tests/ChainKit.Evm.Tests --filter "FullyQualifiedName~WaitForReceiptAsync_ReceiptAppearsAfterTwoPolls"`
Expected: PASS.

- [ ] **Step 3: Add timeout, failure, cancellation, and arg validation tests**

Append:

```csharp
[Fact]
public async Task WaitForReceiptAsync_NeverMined_TimesOut()
{
    _provider.GetTransactionReceiptAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
        .Returns((JsonElement?)null);

    var result = await _client.WaitForReceiptAsync("0xT",
        timeout: TimeSpan.FromMilliseconds(50),
        pollInterval: TimeSpan.FromMilliseconds(10));

    Assert.False(result.Success);
    Assert.Equal(EvmErrorCode.ProviderTimeout, result.ErrorCode);
}

[Fact]
public async Task WaitForReceiptAsync_ConsecutiveFailures_ReturnsProviderConnectionFailed()
{
    _provider.GetTransactionReceiptAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
        .ThrowsAsync(new HttpRequestException("network down"));

    var result = await _client.WaitForReceiptAsync("0xF",
        timeout: TimeSpan.FromSeconds(10),
        pollInterval: TimeSpan.FromMilliseconds(10),
        maxConsecutiveFailures: 3);

    Assert.False(result.Success);
    Assert.Equal(EvmErrorCode.ProviderConnectionFailed, result.ErrorCode);
}

[Fact]
public async Task WaitForReceiptAsync_Cancelled_ThrowsOperationCanceled()
{
    _provider.GetTransactionReceiptAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
        .Returns((JsonElement?)null);

    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        _client.WaitForReceiptAsync("0xC",
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(5),
            ct: cts.Token));
}

[Theory]
[InlineData(null)]
[InlineData("")]
[InlineData("   ")]
public async Task WaitForReceiptAsync_BadTxHash_ReturnsInvalidArgument(string? txHash)
{
    var result = await _client.WaitForReceiptAsync(txHash!);

    Assert.False(result.Success);
    Assert.Equal(EvmErrorCode.InvalidArgument, result.ErrorCode);
    await _provider.DidNotReceive().GetTransactionReceiptAsync(
        Arg.Any<string>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task WaitForReceiptAsync_ZeroPollInterval_ReturnsInvalidArgument()
{
    var result = await _client.WaitForReceiptAsync("0xZP", pollInterval: TimeSpan.Zero);
    Assert.False(result.Success);
    Assert.Equal(EvmErrorCode.InvalidArgument, result.ErrorCode);
}

[Fact]
public async Task WaitForReceiptAsync_NegativeTimeout_ReturnsInvalidArgument()
{
    var result = await _client.WaitForReceiptAsync("0xNT", timeout: TimeSpan.FromSeconds(-1));
    Assert.False(result.Success);
    Assert.Equal(EvmErrorCode.InvalidArgument, result.ErrorCode);
}

[Fact]
public async Task WaitForReceiptAsync_NegativeMaxFailures_ReturnsInvalidArgument()
{
    var result = await _client.WaitForReceiptAsync("0xNF", maxConsecutiveFailures: -1);
    Assert.False(result.Success);
    Assert.Equal(EvmErrorCode.InvalidArgument, result.ErrorCode);
}
```

Run: `dotnet test tests/ChainKit.Evm.Tests --filter "FullyQualifiedName~WaitForReceiptAsync"`
Expected: All PASS.

- [ ] **Step 4: Commit**

```bash
git add src/ChainKit.Evm/EvmClient.cs tests/ChainKit.Evm.Tests/EvmClientTests.cs
git commit -m "$(cat <<'EOF'
feat(evm): add EvmClient.WaitForReceiptAsync

Polls eth_getTransactionReceipt until the tx is mined. Returns the raw
receipt JSON without firing an extra eth_getTransactionByHash. Use when
you only need to confirm inclusion. Same parameter shape as
TronClient.WaitForOnChainAsync (timeout, pollInterval, maxConsecutiveFailures).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Implement `EvmClient.WaitForOnChainAsync`

**Files:**
- Modify: `src/ChainKit.Evm/EvmClient.cs` (append new method)
- Modify: `tests/ChainKit.Evm.Tests/EvmClientTests.cs` (append tests)

Builds on `WaitForReceiptAsync` semantically — same polling loop but also fetches `eth_getTransactionByHash` when the receipt appears, and returns merged `EvmTransactionDetail`.

- [ ] **Step 1: Write failing test — happy path**

Append to `tests/ChainKit.Evm.Tests/EvmClientTests.cs`:

```csharp
// === WaitForOnChainAsync (EVM) ===

private static JsonElement BuildTxData(string from, string to, string valueHex = "0x16345785D8A0000")
{
    // 0x16345785D8A0000 = 0.1 ETH in Wei
    var json = $"{{\"from\":\"{from}\",\"to\":\"{to}\",\"value\":\"{valueHex}\",\"nonce\":\"0x5\"}}";
    using var doc = JsonDocument.Parse(json);
    return doc.RootElement.Clone();
}

[Fact]
public async Task WaitForOnChainAsync_ReceiptAppears_ReturnsDetail()
{
    var calls = 0;
    _provider.GetTransactionReceiptAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
        .Returns(_ =>
        {
            calls++;
            return Task.FromResult<JsonElement?>(calls < 2 ? null : BuildReceipt());
        });
    _provider.GetTransactionByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
        .Returns(BuildTxData(TestAddress, "0x000000000000000000000000000000000000dead"));

    var result = await _client.WaitForOnChainAsync("0xabc",
        timeout: TimeSpan.FromSeconds(5),
        pollInterval: TimeSpan.FromMilliseconds(10));

    Assert.True(result.Success);
    Assert.NotNull(result.Data);
    Assert.Equal(TransactionStatus.Confirmed, result.Data!.Status);
    Assert.Equal(16L, result.Data.BlockNumber); // 0x10
    await _provider.Received(1).GetTransactionByHashAsync(
        Arg.Any<string>(), Arg.Any<CancellationToken>());
}
```

Run: `dotnet test tests/ChainKit.Evm.Tests --filter "FullyQualifiedName~WaitForOnChainAsync_ReceiptAppears"`
Expected: Compile error (method does not exist).

- [ ] **Step 2: Implement `WaitForOnChainAsync`**

Append to `src/ChainKit.Evm/EvmClient.cs`:

```csharp
/// <summary>
/// Polls until the transaction is mined and returns the merged tx + receipt detail.
/// Use after broadcast when a follow-up tx depends on this tx's effects.
/// One additional eth_getTransactionByHash call is made when the receipt appears,
/// to populate sender/recipient/value/nonce. Use WaitForReceiptAsync if you don't need them.
/// </summary>
public async Task<EvmResult<EvmTransactionDetail>> WaitForOnChainAsync(
    string txHash,
    TimeSpan? timeout = null,
    TimeSpan? pollInterval = null,
    int maxConsecutiveFailures = 5,
    CancellationToken ct = default)
{
    var receiptResult = await WaitForReceiptAsync(txHash, timeout, pollInterval, maxConsecutiveFailures, ct);
    if (!receiptResult.Success)
    {
        var code = receiptResult.ErrorCode ?? EvmErrorCode.Unknown;
        var message = receiptResult.Error?.Message ?? "Wait for receipt failed";
        return EvmResult<EvmTransactionDetail>.Fail(code, message);
    }

    try
    {
        var txData = await Provider.GetTransactionByHashAsync(txHash, ct);
        if (txData is null)
            return EvmResult<EvmTransactionDetail>.Fail(
                EvmErrorCode.TransactionNotFound,
                $"Receipt found but eth_getTransactionByHash returned null for {txHash}");

        var detail = BuildTransactionDetail(txHash, txData.Value, receiptResult.Data);
        return EvmResult<EvmTransactionDetail>.Ok(detail);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "WaitForOnChainAsync: post-receipt tx fetch failed for {TxHash}", txHash);
        return EvmResult<EvmTransactionDetail>.Fail(EvmErrorCode.ProviderConnectionFailed, ex.Message);
    }
}
```

Note: `EvmResult<T>` exposes the failure message via `Error?.Message` (the `Error` field is a `ChainError` record from `ChainKit.Core`). `ErrorCode` is `EvmErrorCode?` so we coalesce to `EvmErrorCode.Unknown` defensively (it should always be set when `!Success`).

Run: `dotnet test tests/ChainKit.Evm.Tests --filter "FullyQualifiedName~WaitForOnChainAsync_ReceiptAppears"`
Expected: PASS.

- [ ] **Step 3: Add edge case tests**

Append:

```csharp
[Fact]
public async Task WaitForOnChainAsync_RevertedTx_ReturnsOkWithFailedStatus()
{
    _provider.GetTransactionReceiptAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
        .Returns(BuildReceipt(status: "0x0"));
    _provider.GetTransactionByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
        .Returns(BuildTxData(TestAddress, "0x000000000000000000000000000000000000dead"));

    var result = await _client.WaitForOnChainAsync("0xR",
        timeout: TimeSpan.FromSeconds(5),
        pollInterval: TimeSpan.FromMilliseconds(10));

    Assert.True(result.Success);
    Assert.Equal(TransactionStatus.Failed, result.Data!.Status);
}

[Fact]
public async Task WaitForOnChainAsync_TimeoutBubblesUp()
{
    _provider.GetTransactionReceiptAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
        .Returns((JsonElement?)null);

    var result = await _client.WaitForOnChainAsync("0xT",
        timeout: TimeSpan.FromMilliseconds(50),
        pollInterval: TimeSpan.FromMilliseconds(10));

    Assert.False(result.Success);
    Assert.Equal(EvmErrorCode.ProviderTimeout, result.ErrorCode);
    await _provider.DidNotReceive().GetTransactionByHashAsync(
        Arg.Any<string>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task WaitForOnChainAsync_BadTxHash_ReturnsInvalidArgument()
{
    var result = await _client.WaitForOnChainAsync("");
    Assert.False(result.Success);
    Assert.Equal(EvmErrorCode.InvalidArgument, result.ErrorCode);
}
```

Run: `dotnet test tests/ChainKit.Evm.Tests --filter "FullyQualifiedName~WaitForOnChainAsync_RevertedTx OR FullyQualifiedName~WaitForOnChainAsync_TimeoutBubblesUp OR FullyQualifiedName~WaitForOnChainAsync_BadTxHash"`
Expected: All PASS.

- [ ] **Step 4: Run all `EvmClient` tests for regression**

Run: `dotnet test tests/ChainKit.Evm.Tests --filter "FullyQualifiedName~EvmClientTests"`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/ChainKit.Evm/EvmClient.cs tests/ChainKit.Evm.Tests/EvmClientTests.cs
git commit -m "$(cat <<'EOF'
feat(evm): add EvmClient.WaitForOnChainAsync

Builds on WaitForReceiptAsync — same polling, then one
eth_getTransactionByHash on success to assemble the full
EvmTransactionDetail (sender, recipient, value, status, gas).
Reverted txs (status=0x0) still return Ok with detail.Status=Failed
so callers can decide what to do.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Run full test suite, then update docs

**Files:**
- Modify: `docs/tron-sdk-usage-guide.md`
- Modify: `docs/evm-sdk-usage-guide.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Run full unit-test suite**

Run: `dotnet test --filter "Category!=Integration&Category!=E2E"`
Expected: All tests pass. If anything fails, debug before proceeding to docs.

- [ ] **Step 2: Add Tron usage example**

Open `docs/tron-sdk-usage-guide.md` and find a logical section for "chained transactions" or "wait for confirmation" (likely near `TransferTrxAsync` examples). Append the following section there (adjust the heading level to match surrounding sections):

````markdown
### 鏈式交易：等待上鏈再下一步

Broadcast 成功只代表 tx 進 mempool，還沒上鏈時餘額尚未更新。要立刻從收款地址再轉出，必須等 tx 進塊：

```csharp
var sendAToB = await tronClient.TransferTrxAsync(accountA, walletB, 10m);
if (!sendAToB.Success) { /* handle */ return; }

// 預設 timeout=15s, pollInterval=1s, maxConsecutiveFailures=5
var onChain = await tronClient.WaitForOnChainAsync(sendAToB.Data!.TxId);
if (!onChain.Success)
{
    // ProviderTimeout / ProviderConnectionFailed / InvalidArgument
    return;
}

Console.WriteLine($"Block: {onChain.Data!.BlockNumber}, Result: {onChain.Data.ContractResult}");

// 現在 B 的餘額已更新，可以安全轉出
await tronClient.TransferTrxAsync(accountB, externalWallet, 5m);
```

如果你已經訂閱了 `TronTransactionWatcher` 並在事件流中處理收款，仍可用此方法做兜底（例如下游服務沒掛 watcher）。
````

- [ ] **Step 3: Add EVM usage example**

Open `docs/evm-sdk-usage-guide.md`, find the equivalent `TransferAsync` section, and append:

````markdown
### Chaining transactions: wait for inclusion before the next step

Broadcasting only puts the tx into the node's mempool. Until the tx is mined, the on-chain state (sender / receiver balances) hasn't changed. To safely chain a follow-up tx, wait for inclusion:

```csharp
var sendAToB = await evmClient.TransferAsync(accountA, walletB, 0.1m);
if (!sendAToB.Success) { /* handle */ return; }

// Defaults: timeout=60s, pollInterval=2s, maxConsecutiveFailures=5
var onChain = await evmClient.WaitForOnChainAsync(sendAToB.Data!.TxHash);
if (!onChain.Success)
{
    // ProviderTimeout / ProviderConnectionFailed / InvalidArgument
    return;
}

Console.WriteLine($"Block: {onChain.Data!.BlockNumber}, Status: {onChain.Data.Status}");
if (onChain.Data.Status == TransactionStatus.Failed)
{
    // Tx mined but reverted — caller decides what to do
    return;
}

// Now B's balance is updated, safe to chain.
await evmClient.TransferAsync(accountB, externalWallet, 0.05m);
```

If you only need to know "is it mined" without the full detail, use `WaitForReceiptAsync` — it returns the raw receipt JSON and skips the extra `eth_getTransactionByHash` call.
````

- [ ] **Step 4: Add convention note to `CLAUDE.md`**

Open `CLAUDE.md`. Find the "## 慣例" section (around line 67). Append this single bullet at the end of that list (before the next "##" header):

```markdown
- 鏈式交易要等上鏈：broadcast 後若立刻從收款方再轉出，呼叫端應先 `WaitForOnChainAsync(txId)`（Tron）或 `WaitForOnChainAsync(txHash)` / `WaitForReceiptAsync(txHash)`（EVM），不要假設 broadcast 成功 = 餘額已更新
```

- [ ] **Step 5: Commit docs**

```bash
git add docs/tron-sdk-usage-guide.md docs/evm-sdk-usage-guide.md CLAUDE.md
git commit -m "$(cat <<'EOF'
docs: add WaitForOnChain usage examples and CLAUDE.md convention

Tron + EVM usage guides get a "chained transactions" section showing the
broadcast -> WaitForOnChainAsync -> next-tx flow that avoids "balance is
not sufficient" when chaining. CLAUDE.md gets a one-line convention note.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 6: Final regression run**

Run: `dotnet test --filter "Category!=Integration&Category!=E2E"`
Expected: All tests pass. This is the final check before declaring the feature done.

---

## Optional: Integration tests

These are not required to ship the feature but make a good follow-up if there's time.

- **Tron Nile**: in `tests/ChainKit.Tron.Tests/Integration/`, add a `[Trait("Category", "Integration")]` test that broadcasts a small transfer and asserts `WaitForOnChainAsync` returns Ok within 10 seconds.
- **EVM Anvil**: in `tests/ChainKit.Evm.Tests/Integration/`, add a similar test (Anvil mines instantly, so should complete in 1 poll).

Run: `dotnet test --filter "Category=Integration"`

---

## Done

When all 6 main tasks are complete and `dotnet test --filter "Category!=Integration&Category!=E2E"` is green:

1. Push the branch: `git push -u origin feature/wait-for-onchain`
2. Open PR: `gh pr create --base main --title "feat: WaitForOnChain helpers for Tron and EVM" --body "..."` (per `MEMORY.md` PR workflow on `w6fux5/ChainKit`).
