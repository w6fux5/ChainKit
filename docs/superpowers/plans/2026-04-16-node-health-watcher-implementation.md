# Node Health Watcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add per-chain `TronNodeHealthWatcher` and `EvmNodeHealthWatcher` classes that periodically probe the configured node and raise `OnHealthChecked` events with raw metrics (reachable, latency, block number, block age, chain-ID match for EVM).

**Architecture:** Mirror the existing `TronTransactionWatcher` / `EvmTransactionWatcher` pattern — background Task loop driven by `Task.Delay(intervalMs, ct)`, `IAsyncDisposable` lifecycle, `EventHandler<T>` for subscription. Watchers report raw metrics; callers apply their own health thresholds.

**Tech Stack:** .NET 10, xUnit, NSubstitute, `ITronProvider` / `IEvmProvider` for node access.

**Branch:** Work happens on the existing `develop` branch. Each task ends with a commit. No PR yet — we ship when the user says `發 PR`.

---

## File Structure

**New files (10):**

| Path | Responsibility |
| --- | --- |
| `src/ChainKit.Tron/Watching/TronNodeHealthReport.cs` | Record of a single poll's result |
| `src/ChainKit.Tron/Watching/TronNodeHealthCheckedEventArgs.cs` | EventArgs wrapping report |
| `src/ChainKit.Tron/Watching/TronNodeHealthWatcher.cs` | Main class: poll loop + event |
| `src/ChainKit.Evm/Watching/EvmNodeHealthReport.cs` | EVM record (adds `ChainIdMatch`) |
| `src/ChainKit.Evm/Watching/EvmNodeHealthCheckedEventArgs.cs` | EVM EventArgs |
| `src/ChainKit.Evm/Watching/EvmNodeHealthWatcher.cs` | EVM watcher (includes chainId caching) |
| `tests/ChainKit.Tron.Tests/Watching/TronNodeHealthWatcherTests.cs` | Tron unit tests |
| `tests/ChainKit.Evm.Tests/Watching/EvmNodeHealthWatcherTests.cs` | EVM unit tests |

**Modified files (2):**

| Path | Change |
| --- | --- |
| `src/ChainKit.Evm/Providers/IEvmProvider.cs` | Add `Task<long> GetChainIdAsync(CancellationToken ct = default)` |
| `src/ChainKit.Evm/Providers/EvmHttpProvider.cs` | Implement `GetChainIdAsync` via `eth_chainId` JSON-RPC |

---

## Task 1: Add `GetChainIdAsync` to `IEvmProvider` + `EvmHttpProvider`

**Rationale:** Watcher needs `eth_chainId` to compare against `EvmNetworkConfig.ChainId`. This is a small prerequisite extension — additive, no existing callers affected.

**Files:**
- Modify: `src/ChainKit.Evm/Providers/IEvmProvider.cs`
- Modify: `src/ChainKit.Evm/Providers/EvmHttpProvider.cs`
- Test: `tests/ChainKit.Evm.Tests/Providers/EvmHttpProviderTests.cs` (append to existing file)

- [ ] **Step 1: Locate existing `EvmHttpProviderTests.cs` and read the existing test setup**

Run: `grep -n "public class EvmHttpProviderTests\|SetupHttpResponse\|CreateProvider" tests/ChainKit.Evm.Tests/Providers/EvmHttpProviderTests.cs | head`

Expected output: existing helper methods for setting up mock HTTP responses. Note the helper names — you will reuse them to keep style consistent.

- [ ] **Step 2: Append a failing test for `GetChainIdAsync`**

Append to `tests/ChainKit.Evm.Tests/Providers/EvmHttpProviderTests.cs` (inside the existing test class, before the closing brace):

```csharp
    [Fact]
    public async Task GetChainIdAsync_ReturnsDecodedChainId()
    {
        // eth_chainId returns a 0x-prefixed hex string; 0x1 = 1 (Ethereum mainnet)
        SetupRpcResponse("eth_chainId", "\"0x1\"");

        using var provider = CreateProvider();
        var chainId = await provider.GetChainIdAsync();

        Assert.Equal(1L, chainId);
    }

    [Fact]
    public async Task GetChainIdAsync_LargeChainId_DecodesCorrectly()
    {
        // 0x89 = 137 (Polygon mainnet)
        SetupRpcResponse("eth_chainId", "\"0x89\"");

        using var provider = CreateProvider();
        var chainId = await provider.GetChainIdAsync();

        Assert.Equal(137L, chainId);
    }
```

**Note:** If the helpers in the existing file have different names (e.g. `SetupJsonRpc` instead of `SetupRpcResponse`), adapt these two tests to match. The shape of the asserts stays the same.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ChainKit.Evm.Tests --nologo --filter "FullyQualifiedName~GetChainIdAsync"`

Expected: compile error `'IEvmProvider' does not contain a definition for 'GetChainIdAsync'` — the method does not exist yet.

- [ ] **Step 4: Add the method to the interface**

Edit `src/ChainKit.Evm/Providers/IEvmProvider.cs`, appending inside the interface body (just before the closing `}`):

```csharp
    /// <summary>
    /// Gets the chain ID reported by the node via eth_chainId.
    /// </summary>
    Task<long> GetChainIdAsync(CancellationToken ct = default);
```

- [ ] **Step 5: Implement in `EvmHttpProvider`**

Edit `src/ChainKit.Evm/Providers/EvmHttpProvider.cs`. Find an existing simple method for shape reference (e.g. `GetBlockNumberAsync`) — it will show you the RPC-call + hex-decode pattern used here.

Append the following method inside `EvmHttpProvider` (place near `GetBlockNumberAsync` for locality):

```csharp
    /// <summary>
    /// Gets the chain ID reported by the node via eth_chainId.
    /// </summary>
    public async Task<long> GetChainIdAsync(CancellationToken ct = default)
    {
        var response = await RpcAsync("eth_chainId", Array.Empty<object>(), ct);
        using var doc = JsonDocument.Parse(response);
        var hex = doc.RootElement.GetProperty("result").GetString()
            ?? throw new ChainKitException("eth_chainId returned null");
        return Convert.ToInt64(hex, 16);
    }
```

**Note:** If the existing RPC helper is named differently (e.g. `CallRpcAsync` or `SendRpcAsync`), use that name instead. Read one existing method in the file first to confirm the exact helper signature.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ChainKit.Evm.Tests --nologo --filter "FullyQualifiedName~GetChainIdAsync"`

Expected: PASSED (2/2).

- [ ] **Step 7: Run the full EVM test suite to verify nothing regressed**

Run: `dotnet test tests/ChainKit.Evm.Tests --nologo --filter "Category!=Integration&Category!=E2E"`

Expected: all tests pass (previous count + 2).

- [ ] **Step 8: Commit**

```bash
git add src/ChainKit.Evm/Providers/IEvmProvider.cs src/ChainKit.Evm/Providers/EvmHttpProvider.cs tests/ChainKit.Evm.Tests/Providers/EvmHttpProviderTests.cs
git commit -m "feat: add IEvmProvider.GetChainIdAsync for health-watcher support

Adds eth_chainId JSON-RPC query to IEvmProvider interface and implements it
in EvmHttpProvider. Additive extension — no existing callers affected.
Prerequisite for EvmNodeHealthWatcher.ChainIdMatch field.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Tron watcher happy path (types + basic poll)

**Rationale:** Write the first Tron test, then create all the supporting types (Report, EventArgs, Watcher class) and the minimum poll-loop needed to make it pass.

**Files:**
- Create: `src/ChainKit.Tron/Watching/TronNodeHealthReport.cs`
- Create: `src/ChainKit.Tron/Watching/TronNodeHealthCheckedEventArgs.cs`
- Create: `src/ChainKit.Tron/Watching/TronNodeHealthWatcher.cs`
- Create: `tests/ChainKit.Tron.Tests/Watching/TronNodeHealthWatcherTests.cs`

- [ ] **Step 1: Read the existing transaction watcher to understand the pattern**

Run: `grep -n "class TronTransactionWatcher\|StartAsync\|StopAsync\|DisposeAsync" src/ChainKit.Tron/Watching/TronTransactionWatcher.cs | head -20`

Expected: see how `TronTransactionWatcher` structures its background loop, cancellation, dispose. Match this style — same XML doc placement, same logger pattern, same cancellation approach.

- [ ] **Step 2: Write the first failing test**

Create `tests/ChainKit.Tron.Tests/Watching/TronNodeHealthWatcherTests.cs`:

```csharp
using ChainKit.Tron.Models;
using ChainKit.Tron.Providers;
using ChainKit.Tron.Watching;
using NSubstitute;
using Xunit;

namespace ChainKit.Tron.Tests.Watching;

public class TronNodeHealthWatcherTests
{
    private readonly ITronProvider _provider = Substitute.For<ITronProvider>();

    private static BlockInfo CreateBlock(long number, long timestampMs)
        => new(number, "00000000000000" + number.ToString("X16"), timestampMs, 0, new byte[34]);

    [Fact]
    public async Task OnHealthChecked_FiresAfterFirstPoll()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(CreateBlock(1000, nowMs - 2000));

        await using var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 50);

        var tcs = new TaskCompletionSource<TronNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => tcs.TrySetResult(e.Report);

        await watcher.StartAsync();
        var report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(report.Reachable);
        Assert.Null(report.Error);
        Assert.Equal(1000L, report.BlockNumber);
        Assert.NotNull(report.BlockAge);
        Assert.True(report.BlockAge >= TimeSpan.FromSeconds(1), "BlockAge should be ~2s given block timestamp 2s ago");
    }
}
```

**Note on `BlockInfo` signature:** The constructor shape above matches the existing test helpers (see `tests/ChainKit.Tron.Tests/TronClientTests.cs`). If `BlockInfo` has a different shape in this codebase, open that file and copy the existing `new BlockInfo(...)` call style.

- [ ] **Step 3: Run test to verify it fails (compile error)**

Run: `dotnet test tests/ChainKit.Tron.Tests --nologo --filter "FullyQualifiedName~TronNodeHealthWatcherTests"`

Expected: compile error mentioning `TronNodeHealthWatcher`, `TronNodeHealthReport`, or `Watching` namespace members that don't exist yet.

- [ ] **Step 4: Create the report record**

Create `src/ChainKit.Tron/Watching/TronNodeHealthReport.cs`:

```csharp
namespace ChainKit.Tron.Watching;

/// <summary>
/// Snapshot of one node health check. <see cref="Reachable"/> tells you whether the probe
/// succeeded; when false, all numeric fields are null and <see cref="Error"/> holds the reason.
/// </summary>
/// <param name="Timestamp">When the check ran (UTC).</param>
/// <param name="Reachable">Whether the node responded successfully to the probe.</param>
/// <param name="Latency">Round-trip duration of the probe. Populated even on failure.</param>
/// <param name="BlockNumber">Latest block number; null when <see cref="Reachable"/> is false.</param>
/// <param name="BlockAge">How old the latest block is (UtcNow - block timestamp, clamped to >= 0); null when <see cref="Reachable"/> is false.</param>
/// <param name="Error">Exception message when <see cref="Reachable"/> is false; null on success.</param>
public sealed record TronNodeHealthReport(
    DateTimeOffset Timestamp,
    bool Reachable,
    TimeSpan Latency,
    long? BlockNumber,
    TimeSpan? BlockAge,
    string? Error);
```

- [ ] **Step 5: Create the event args**

Create `src/ChainKit.Tron/Watching/TronNodeHealthCheckedEventArgs.cs`:

```csharp
namespace ChainKit.Tron.Watching;

/// <summary>
/// Event args for <see cref="TronNodeHealthWatcher.OnHealthChecked"/>.
/// </summary>
public sealed class TronNodeHealthCheckedEventArgs : EventArgs
{
    /// <summary>The report produced by this poll.</summary>
    public TronNodeHealthReport Report { get; }

    /// <summary>Creates event args wrapping the given report.</summary>
    public TronNodeHealthCheckedEventArgs(TronNodeHealthReport report)
    {
        Report = report;
    }
}
```

- [ ] **Step 6: Create the watcher class with minimal poll loop**

Create `src/ChainKit.Tron/Watching/TronNodeHealthWatcher.cs`:

```csharp
using ChainKit.Tron.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChainKit.Tron.Watching;

/// <summary>
/// Periodically probes the configured Tron node and raises <see cref="OnHealthChecked"/>
/// events with raw metrics. The watcher reports; the caller decides what "healthy" means.
/// </summary>
public sealed class TronNodeHealthWatcher : IAsyncDisposable
{
    private readonly ITronProvider _provider;
    private readonly int _intervalMs;
    private readonly ILogger<TronNodeHealthWatcher> _logger;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    /// <summary>Raised after every poll, whether it succeeded or failed.</summary>
    public event EventHandler<TronNodeHealthCheckedEventArgs>? OnHealthChecked;

    /// <summary>
    /// Creates a new health watcher. Call <see cref="StartAsync"/> to begin polling.
    /// </summary>
    /// <param name="provider">The Tron provider to probe.</param>
    /// <param name="intervalMs">Interval between polls in milliseconds. Default 5000.</param>
    /// <param name="logger">Optional logger. Defaults to NullLogger.</param>
    public TronNodeHealthWatcher(
        ITronProvider provider,
        int intervalMs = 5000,
        ILogger<TronNodeHealthWatcher>? logger = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _intervalMs = intervalMs;
        _logger = logger ?? NullLogger<TronNodeHealthWatcher>.Instance;
    }

    /// <summary>Starts the polling loop. Subsequent calls are no-ops while running.</summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        if (_loopTask is not null) return Task.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = Task.Run(() => PollLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    /// <summary>Stops the polling loop. Safe to call multiple times; safe to call before Start.</summary>
    public async Task StopAsync()
    {
        if (_cts is null) return;
        _cts.Cancel();
        try
        {
            if (_loopTask is not null) await _loopTask;
        }
        catch (OperationCanceledException) { /* expected */ }
        _cts.Dispose();
        _cts = null;
        _loopTask = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var report = await ProbeAsync(ct);
            try
            {
                OnHealthChecked?.Invoke(this, new TronNodeHealthCheckedEventArgs(report));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OnHealthChecked handler threw; continuing polling");
            }

            try
            {
                await Task.Delay(_intervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<TronNodeHealthReport> ProbeAsync(CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var block = await _provider.GetNowBlockAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var latency = now - startedAt;
            var blockTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(block.Timestamp);
            var blockAge = now - blockTimestamp;
            if (blockAge < TimeSpan.Zero) blockAge = TimeSpan.Zero;

            return new TronNodeHealthReport(
                Timestamp: now,
                Reachable: true,
                Latency: latency,
                BlockNumber: block.Number,
                BlockAge: blockAge,
                Error: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var now = DateTimeOffset.UtcNow;
            return new TronNodeHealthReport(
                Timestamp: now,
                Reachable: false,
                Latency: now - startedAt,
                BlockNumber: null,
                BlockAge: null,
                Error: ex.Message);
        }
    }
}
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test tests/ChainKit.Tron.Tests --nologo --filter "FullyQualifiedName~TronNodeHealthWatcherTests"`

Expected: PASSED (1/1). If `BlockInfo` field names differ from `Number` / `Timestamp`, fix the `ProbeAsync` method to use the correct names (check the real type via `grep -n "record BlockInfo\|class BlockInfo" src/ChainKit.Tron/Models/`).

- [ ] **Step 8: Run full Tron test suite to verify no regression**

Run: `dotnet test tests/ChainKit.Tron.Tests --nologo --filter "Category!=Integration&Category!=E2E"`

Expected: all tests pass (previous count + 1).

- [ ] **Step 9: Commit**

```bash
git add src/ChainKit.Tron/Watching/TronNodeHealthReport.cs \
        src/ChainKit.Tron/Watching/TronNodeHealthCheckedEventArgs.cs \
        src/ChainKit.Tron/Watching/TronNodeHealthWatcher.cs \
        tests/ChainKit.Tron.Tests/Watching/TronNodeHealthWatcherTests.cs
git commit -m "feat: add TronNodeHealthWatcher with happy-path polling

Introduces per-chain health observation: TronNodeHealthWatcher polls
GetNowBlockAsync at a configurable interval and raises OnHealthChecked
with raw metrics (Reachable, Latency, BlockNumber, BlockAge). Callers
decide what 'healthy' means.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Tron watcher resilience (provider failure + polling continues)

**Rationale:** Cover the failure paths — provider throws, report shows unreachable, polling keeps going.

**Files:**
- Modify: `tests/ChainKit.Tron.Tests/Watching/TronNodeHealthWatcherTests.cs` (append)

- [ ] **Step 1: Append failure-path tests**

Append inside the existing `TronNodeHealthWatcherTests` class (before the closing brace):

```csharp
    [Fact]
    public async Task OnHealthChecked_ProviderThrows_ReportShowsUnreachable()
    {
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        await using var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 50);
        var tcs = new TaskCompletionSource<TronNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => tcs.TrySetResult(e.Report);

        await watcher.StartAsync();
        var report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(report.Reachable);
        Assert.Null(report.BlockNumber);
        Assert.Null(report.BlockAge);
        Assert.NotNull(report.Error);
        Assert.Contains("connection refused", report.Error);
    }

    [Fact]
    public async Task OnHealthChecked_AfterFailure_KeepsPolling()
    {
        var callCount = 0;
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref callCount);
                if (callCount == 1) throw new HttpRequestException("fail");
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return CreateBlock(500, nowMs);
            });

        await using var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 30);
        var reports = new List<TronNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) =>
        {
            lock (reports) reports.Add(e.Report);
        };

        await watcher.StartAsync();
        await Task.Delay(200);
        await watcher.StopAsync();

        List<TronNodeHealthReport> snapshot;
        lock (reports) snapshot = [.. reports];

        Assert.True(snapshot.Count >= 2, $"Expected at least 2 reports, got {snapshot.Count}");
        Assert.False(snapshot[0].Reachable);
        Assert.Contains(snapshot, r => r.Reachable);
    }

    [Fact]
    public async Task OnHealthChecked_HandlerThrows_DoesNotStopPolling()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(CreateBlock(1, nowMs));

        await using var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 30);
        var count = 0;
        watcher.OnHealthChecked += (_, _) =>
        {
            Interlocked.Increment(ref count);
            throw new InvalidOperationException("handler blew up");
        };

        await watcher.StartAsync();
        await Task.Delay(200);
        await watcher.StopAsync();

        Assert.True(count >= 2, $"Expected at least 2 handler invocations, got {count}");
    }
```

- [ ] **Step 2: Run tests — they should already pass**

Run: `dotnet test tests/ChainKit.Tron.Tests --nologo --filter "FullyQualifiedName~TronNodeHealthWatcherTests"`

Expected: PASSED (4/4). The implementation from Task 2 already handles these cases. If any fail, debug — typical cause is missing `await using` or a flaky timing assertion (bump delay if the test runs on a loaded CI machine).

- [ ] **Step 3: Commit**

```bash
git add tests/ChainKit.Tron.Tests/Watching/TronNodeHealthWatcherTests.cs
git commit -m "test: cover Tron health watcher failure + resilience paths

Verifies: provider-throw produces Reachable=false report; polling
continues across failures; event handler exceptions are swallowed and
do not stop the loop.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Tron watcher lifecycle tests (Start/Stop/Dispose idempotency)

**Files:**
- Modify: `tests/ChainKit.Tron.Tests/Watching/TronNodeHealthWatcherTests.cs` (append)

- [ ] **Step 1: Append lifecycle tests**

Append inside `TronNodeHealthWatcherTests`:

```csharp
    [Fact]
    public async Task StopAsync_StopsPolling()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(CreateBlock(1, nowMs));

        var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 20);
        var count = 0;
        watcher.OnHealthChecked += (_, _) => Interlocked.Increment(ref count);

        await watcher.StartAsync();
        await Task.Delay(100);
        await watcher.StopAsync();
        var snapshotAfterStop = count;

        await Task.Delay(150);
        Assert.Equal(snapshotAfterStop, count);
    }

    [Fact]
    public async Task StartAsync_Twice_DoesNotDoubleStart()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(CreateBlock(1, nowMs));

        await using var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 50);
        await watcher.StartAsync();
        await watcher.StartAsync(); // must be no-op, not throw
        await Task.Delay(120);
        await watcher.StopAsync();

        // We cannot easily assert "only one loop" from the outside; instead, verify that
        // after Stop the watcher can be cleanly reclaimed. Reaching this line without
        // hang or exception demonstrates idempotency.
    }

    [Fact]
    public async Task StopAsync_BeforeStart_IsNoOp()
    {
        var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 50);
        var ex = await Record.ExceptionAsync(() => watcher.StopAsync());
        Assert.Null(ex);
    }

    [Fact]
    public async Task DisposeAsync_CancelsGracefully()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(CreateBlock(1, nowMs));

        var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 20);
        await watcher.StartAsync();
        await Task.Delay(80);

        var disposeTask = watcher.DisposeAsync().AsTask();
        var finished = await Task.WhenAny(disposeTask, Task.Delay(2000));
        Assert.Same(disposeTask, finished); // finished within 2s (didn't hang)
    }
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/ChainKit.Tron.Tests --nologo --filter "FullyQualifiedName~TronNodeHealthWatcherTests"`

Expected: PASSED (8/8).

- [ ] **Step 3: Commit**

```bash
git add tests/ChainKit.Tron.Tests/Watching/TronNodeHealthWatcherTests.cs
git commit -m "test: cover Tron health watcher lifecycle (Start/Stop/Dispose idempotency)

Verifies Stop halts polling, Start-twice is a no-op, Stop-before-Start is
a no-op, and DisposeAsync completes within bounded time without hanging.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Tron watcher timing tests (periodic poll + BlockAge)

**Files:**
- Modify: `tests/ChainKit.Tron.Tests/Watching/TronNodeHealthWatcherTests.cs` (append)

- [ ] **Step 1: Append timing tests**

```csharp
    [Fact]
    public async Task OnHealthChecked_FiresRepeatedlyAtInterval()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(CreateBlock(1, nowMs));

        await using var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 30);
        var count = 0;
        watcher.OnHealthChecked += (_, _) => Interlocked.Increment(ref count);

        await watcher.StartAsync();
        await Task.Delay(250);
        await watcher.StopAsync();

        // At 30ms interval over 250ms we expect ~5-8 fires; allow slack for loaded CI.
        Assert.True(count >= 3, $"Expected at least 3 periodic fires, got {count}");
    }

    [Fact]
    public async Task BlockAge_ComputedRelativeToUtcNow()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(CreateBlock(1, nowMs - 5000)); // block 5s old

        await using var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 50);
        var tcs = new TaskCompletionSource<TronNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => tcs.TrySetResult(e.Report);

        await watcher.StartAsync();
        var report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(report.BlockAge);
        Assert.True(report.BlockAge >= TimeSpan.FromSeconds(4), $"BlockAge={report.BlockAge}");
        Assert.True(report.BlockAge < TimeSpan.FromSeconds(10), $"BlockAge={report.BlockAge}");
    }

    [Fact]
    public async Task BlockAge_FutureBlockTimestamp_ClampedToZero()
    {
        // Malicious/misconfigured node returns a block from 60s in the future
        var futureMs = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeMilliseconds();
        _provider.GetNowBlockAsync(Arg.Any<CancellationToken>())
            .Returns(CreateBlock(1, futureMs));

        await using var watcher = new TronNodeHealthWatcher(_provider, intervalMs: 50);
        var tcs = new TaskCompletionSource<TronNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => tcs.TrySetResult(e.Report);

        await watcher.StartAsync();
        var report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.Zero, report.BlockAge);
    }
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/ChainKit.Tron.Tests --nologo --filter "FullyQualifiedName~TronNodeHealthWatcherTests"`

Expected: PASSED (11/11).

- [ ] **Step 3: Run full test suite**

Run: `dotnet test --nologo --filter "Category!=Integration&Category!=E2E"`

Expected: all tests pass. Count should be previous + 11 Tron tests.

- [ ] **Step 4: Commit**

```bash
git add tests/ChainKit.Tron.Tests/Watching/TronNodeHealthWatcherTests.cs
git commit -m "test: cover Tron health watcher timing (interval firing + BlockAge)

Verifies OnHealthChecked fires multiple times within the interval window
and that BlockAge is computed relative to UtcNow, clamping future-dated
block timestamps to zero.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: EVM watcher happy path (types + basic poll, no chainId yet)

**Rationale:** Mirror the Tron watcher structure. Defer chainId behavior to Task 7 so this task stays focused.

**Files:**
- Create: `src/ChainKit.Evm/Watching/EvmNodeHealthReport.cs`
- Create: `src/ChainKit.Evm/Watching/EvmNodeHealthCheckedEventArgs.cs`
- Create: `src/ChainKit.Evm/Watching/EvmNodeHealthWatcher.cs`
- Create: `tests/ChainKit.Evm.Tests/Watching/EvmNodeHealthWatcherTests.cs`

- [ ] **Step 1: Read `EvmTransactionWatcher` + `EvmNetworkConfig` for pattern / types**

Run:
```
grep -n "class EvmNetworkConfig\|ChainId" src/ChainKit.Evm/Providers/EvmNetworkConfig.cs
grep -n "class EvmTransactionWatcher\|StartAsync\|DisposeAsync" src/ChainKit.Evm/Watching/EvmTransactionWatcher.cs | head -10
```

Expected: confirm `EvmNetworkConfig` has a `long ChainId` property and see the watcher lifecycle style.

- [ ] **Step 2: Write the first failing EVM test**

Create `tests/ChainKit.Evm.Tests/Watching/EvmNodeHealthWatcherTests.cs`:

```csharp
using System.Numerics;
using System.Text.Json;
using ChainKit.Evm.Providers;
using ChainKit.Evm.Watching;
using NSubstitute;
using Xunit;

namespace ChainKit.Evm.Tests.Watching;

public class EvmNodeHealthWatcherTests
{
    private readonly IEvmProvider _provider = Substitute.For<IEvmProvider>();
    private readonly EvmNetworkConfig _network = new("Ethereum", 1L, "https://rpc", 12);

    private static JsonElement BlockWithTimestamp(long unixSeconds)
    {
        var json = $"{{\"timestamp\":\"0x{unixSeconds:x}\"}}";
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public async Task OnHealthChecked_FiresAfterFirstPoll_WithBlockData()
    {
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(999L);
        _provider.GetBlockByNumberAsync(999L, false, Arg.Any<CancellationToken>())
            .Returns(BlockWithTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(1L);

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 50);
        var tcs = new TaskCompletionSource<EvmNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => tcs.TrySetResult(e.Report);

        await watcher.StartAsync();
        var report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(report.Reachable);
        Assert.Equal(999L, report.BlockNumber);
        Assert.NotNull(report.BlockAge);
        Assert.True(report.BlockAge >= TimeSpan.FromSeconds(2));
    }
}
```

**Note on `EvmNetworkConfig` constructor:** The shape above matches the existing usage in `Erc20Contract`. If the ctor has different args (e.g. a `BlockTime` or `NativeSymbol` field not shown), adapt accordingly — run `grep -n "public.*EvmNetworkConfig\|record EvmNetworkConfig" src/ChainKit.Evm/Providers/EvmNetworkConfig.cs` and copy the real shape.

- [ ] **Step 3: Run test — should fail to compile**

Run: `dotnet test tests/ChainKit.Evm.Tests --nologo --filter "FullyQualifiedName~EvmNodeHealthWatcherTests"`

Expected: compile error for `EvmNodeHealthWatcher`, `EvmNodeHealthReport`, `Watching` types.

- [ ] **Step 4: Create the EVM report record**

Create `src/ChainKit.Evm/Watching/EvmNodeHealthReport.cs`:

```csharp
namespace ChainKit.Evm.Watching;

/// <summary>
/// Snapshot of one EVM node health check. Adds <see cref="ChainIdMatch"/> compared to the Tron equivalent.
/// </summary>
/// <param name="Timestamp">When the check ran (UTC).</param>
/// <param name="Reachable">Whether the node responded successfully.</param>
/// <param name="Latency">Round-trip duration of the probe. Populated even on failure.</param>
/// <param name="BlockNumber">Latest block number; null when Reachable is false.</param>
/// <param name="BlockAge">How old the latest block is; null when Reachable is false.</param>
/// <param name="ChainIdMatch">Whether the node-reported chain ID matches the configured <see cref="EvmNetworkConfig.ChainId"/>. Null until first successful eth_chainId.</param>
/// <param name="Error">Exception message when Reachable is false; null on success.</param>
public sealed record EvmNodeHealthReport(
    DateTimeOffset Timestamp,
    bool Reachable,
    TimeSpan Latency,
    long? BlockNumber,
    TimeSpan? BlockAge,
    bool? ChainIdMatch,
    string? Error);
```

- [ ] **Step 5: Create the EVM event args**

Create `src/ChainKit.Evm/Watching/EvmNodeHealthCheckedEventArgs.cs`:

```csharp
namespace ChainKit.Evm.Watching;

/// <summary>
/// Event args for <see cref="EvmNodeHealthWatcher.OnHealthChecked"/>.
/// </summary>
public sealed class EvmNodeHealthCheckedEventArgs : EventArgs
{
    /// <summary>The report produced by this poll.</summary>
    public EvmNodeHealthReport Report { get; }

    /// <summary>Creates event args wrapping the given report.</summary>
    public EvmNodeHealthCheckedEventArgs(EvmNodeHealthReport report)
    {
        Report = report;
    }
}
```

- [ ] **Step 6: Create the EVM watcher**

Create `src/ChainKit.Evm/Watching/EvmNodeHealthWatcher.cs`:

```csharp
using System.Text.Json;
using ChainKit.Evm.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChainKit.Evm.Watching;

/// <summary>
/// Periodically probes the configured EVM node and raises <see cref="OnHealthChecked"/>
/// events with raw metrics. The watcher reports; the caller decides what "healthy" means.
/// Chain ID is queried once via eth_chainId on the first successful poll and cached.
/// </summary>
public sealed class EvmNodeHealthWatcher : IAsyncDisposable
{
    private readonly IEvmProvider _provider;
    private readonly EvmNetworkConfig _network;
    private readonly int _intervalMs;
    private readonly ILogger<EvmNodeHealthWatcher> _logger;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private long? _cachedChainId; // null until first successful eth_chainId

    /// <summary>Raised after every poll, whether it succeeded or failed.</summary>
    public event EventHandler<EvmNodeHealthCheckedEventArgs>? OnHealthChecked;

    /// <summary>
    /// Creates a new health watcher.
    /// </summary>
    /// <param name="provider">The EVM provider to probe.</param>
    /// <param name="network">Network config providing the expected ChainId.</param>
    /// <param name="intervalMs">Interval between polls in milliseconds. Default 5000.</param>
    /// <param name="logger">Optional logger. Defaults to NullLogger.</param>
    public EvmNodeHealthWatcher(
        IEvmProvider provider,
        EvmNetworkConfig network,
        int intervalMs = 5000,
        ILogger<EvmNodeHealthWatcher>? logger = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _intervalMs = intervalMs;
        _logger = logger ?? NullLogger<EvmNodeHealthWatcher>.Instance;
    }

    /// <summary>Starts the polling loop. Subsequent calls are no-ops while running.</summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        if (_loopTask is not null) return Task.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = Task.Run(() => PollLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    /// <summary>Stops the polling loop. Safe to call multiple times; safe to call before Start.</summary>
    public async Task StopAsync()
    {
        if (_cts is null) return;
        _cts.Cancel();
        try
        {
            if (_loopTask is not null) await _loopTask;
        }
        catch (OperationCanceledException) { /* expected */ }
        _cts.Dispose();
        _cts = null;
        _loopTask = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var report = await ProbeAsync(ct);
            try
            {
                OnHealthChecked?.Invoke(this, new EvmNodeHealthCheckedEventArgs(report));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OnHealthChecked handler threw; continuing polling");
            }

            try
            {
                await Task.Delay(_intervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<EvmNodeHealthReport> ProbeAsync(CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var blockNumber = await _provider.GetBlockNumberAsync(ct);
            var blockElement = await _provider.GetBlockByNumberAsync(blockNumber, false, ct);
            var blockAge = blockElement is { } be ? ComputeBlockAge(be) : (TimeSpan?)null;

            // ChainId: fetch once on first successful poll, cache thereafter.
            if (_cachedChainId is null)
            {
                try { _cachedChainId = await _provider.GetChainIdAsync(ct); }
                catch { /* leave cache null; ChainIdMatch stays null this cycle */ }
            }
            bool? chainIdMatch = _cachedChainId is long c ? c == _network.ChainId : null;

            var now = DateTimeOffset.UtcNow;
            return new EvmNodeHealthReport(
                Timestamp: now,
                Reachable: true,
                Latency: now - startedAt,
                BlockNumber: blockNumber,
                BlockAge: blockAge,
                ChainIdMatch: chainIdMatch,
                Error: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var now = DateTimeOffset.UtcNow;
            return new EvmNodeHealthReport(
                Timestamp: now,
                Reachable: false,
                Latency: now - startedAt,
                BlockNumber: null,
                BlockAge: null,
                ChainIdMatch: _cachedChainId is long c ? c == _network.ChainId : null,
                Error: ex.Message);
        }
    }

    private static TimeSpan ComputeBlockAge(JsonElement block)
    {
        if (!block.TryGetProperty("timestamp", out var tsEl)) return TimeSpan.Zero;
        var tsHex = tsEl.GetString();
        if (string.IsNullOrEmpty(tsHex)) return TimeSpan.Zero;
        if (tsHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) tsHex = tsHex[2..];
        if (!long.TryParse(tsHex, System.Globalization.NumberStyles.HexNumber, null, out var tsSeconds))
            return TimeSpan.Zero;

        var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(tsSeconds);
        return age < TimeSpan.Zero ? TimeSpan.Zero : age;
    }
}
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/ChainKit.Evm.Tests --nologo --filter "FullyQualifiedName~EvmNodeHealthWatcherTests"`

Expected: PASSED (1/1). If it fails, check `EvmNetworkConfig` ctor args (see note under Step 2) and `GetBlockByNumberAsync` signature.

- [ ] **Step 8: Run full EVM suite**

Run: `dotnet test tests/ChainKit.Evm.Tests --nologo --filter "Category!=Integration&Category!=E2E"`

Expected: all tests pass.

- [ ] **Step 9: Commit**

```bash
git add src/ChainKit.Evm/Watching/EvmNodeHealthReport.cs \
        src/ChainKit.Evm/Watching/EvmNodeHealthCheckedEventArgs.cs \
        src/ChainKit.Evm/Watching/EvmNodeHealthWatcher.cs \
        tests/ChainKit.Evm.Tests/Watching/EvmNodeHealthWatcherTests.cs
git commit -m "feat: add EvmNodeHealthWatcher with happy-path polling

Mirrors TronNodeHealthWatcher for EVM chains: polls block number + block
timestamp, reports Reachable/Latency/BlockNumber/BlockAge. Chain ID is
queried once via eth_chainId on the first successful poll and cached.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: EVM watcher chainId behavior (match, mismatch, caching)

**Files:**
- Modify: `tests/ChainKit.Evm.Tests/Watching/EvmNodeHealthWatcherTests.cs` (append)

- [ ] **Step 1: Append chainId tests**

```csharp
    [Fact]
    public async Task ChainIdMatch_True_WhenNodeChainIdMatchesConfig()
    {
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _provider.GetBlockByNumberAsync(1L, false, Arg.Any<CancellationToken>())
            .Returns(BlockWithTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(1L); // matches _network

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 50);
        var tcs = new TaskCompletionSource<EvmNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => tcs.TrySetResult(e.Report);

        await watcher.StartAsync();
        var report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(report.ChainIdMatch);
    }

    [Fact]
    public async Task ChainIdMatch_False_WhenNodeChainIdDiffers()
    {
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _provider.GetBlockByNumberAsync(1L, false, Arg.Any<CancellationToken>())
            .Returns(BlockWithTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(137L); // Polygon

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 50);
        var tcs = new TaskCompletionSource<EvmNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => tcs.TrySetResult(e.Report);

        await watcher.StartAsync();
        var report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(report.ChainIdMatch);
    }

    [Fact]
    public async Task ChainIdMatch_CachedAfterFirstSuccess()
    {
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _provider.GetBlockByNumberAsync(1L, false, Arg.Any<CancellationToken>())
            .Returns(BlockWithTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(1L);

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 20);
        var count = 0;
        watcher.OnHealthChecked += (_, _) => Interlocked.Increment(ref count);

        await watcher.StartAsync();
        await Task.Delay(200);
        await watcher.StopAsync();

        Assert.True(count >= 3, $"Expected multiple polls; got {count}");
        // eth_chainId must be called exactly once despite multiple polls
        await _provider.Received(1).GetChainIdAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChainIdMatch_Null_BeforeFirstSuccessfulFetch()
    {
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("node down"));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("node down"));

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 50);
        var tcs = new TaskCompletionSource<EvmNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => tcs.TrySetResult(e.Report);

        await watcher.StartAsync();
        var report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(report.Reachable);
        Assert.Null(report.ChainIdMatch);
    }
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/ChainKit.Evm.Tests --nologo --filter "FullyQualifiedName~EvmNodeHealthWatcherTests"`

Expected: PASSED (5/5).

- [ ] **Step 3: Commit**

```bash
git add tests/ChainKit.Evm.Tests/Watching/EvmNodeHealthWatcherTests.cs
git commit -m "test: cover EVM health watcher ChainIdMatch semantics + caching

Verifies ChainIdMatch is true/false based on node-reported chain ID vs
configured network; eth_chainId is called exactly once and cached;
ChainIdMatch stays null when chain ID has never been successfully fetched.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: EVM watcher resilience + lifecycle tests (parallel to Tron Tasks 3-4)

**Files:**
- Modify: `tests/ChainKit.Evm.Tests/Watching/EvmNodeHealthWatcherTests.cs` (append)

- [ ] **Step 1: Append resilience + lifecycle tests**

```csharp
    [Fact]
    public async Task OnHealthChecked_ProviderThrows_ReportShowsUnreachable()
    {
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 50);
        var tcs = new TaskCompletionSource<EvmNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => tcs.TrySetResult(e.Report);

        await watcher.StartAsync();
        var report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(report.Reachable);
        Assert.Null(report.BlockNumber);
        Assert.Null(report.BlockAge);
        Assert.Contains("connection refused", report.Error!);
    }

    [Fact]
    public async Task OnHealthChecked_AfterFailure_KeepsPolling()
    {
        var callCount = 0;
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref callCount);
                if (callCount == 1) throw new HttpRequestException("fail");
                return 7L;
            });
        _provider.GetBlockByNumberAsync(7L, false, Arg.Any<CancellationToken>())
            .Returns(BlockWithTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(1L);

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 30);
        var reports = new List<EvmNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => { lock (reports) reports.Add(e.Report); };

        await watcher.StartAsync();
        await Task.Delay(200);
        await watcher.StopAsync();

        List<EvmNodeHealthReport> snapshot;
        lock (reports) snapshot = [.. reports];

        Assert.True(snapshot.Count >= 2);
        Assert.False(snapshot[0].Reachable);
        Assert.Contains(snapshot, r => r.Reachable);
    }

    [Fact]
    public async Task OnHealthChecked_HandlerThrows_DoesNotStopPolling()
    {
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _provider.GetBlockByNumberAsync(1L, false, Arg.Any<CancellationToken>())
            .Returns(BlockWithTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(1L);

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 30);
        var count = 0;
        watcher.OnHealthChecked += (_, _) =>
        {
            Interlocked.Increment(ref count);
            throw new InvalidOperationException("handler blew up");
        };

        await watcher.StartAsync();
        await Task.Delay(200);
        await watcher.StopAsync();

        Assert.True(count >= 2);
    }

    [Fact]
    public async Task StopAsync_StopsPolling()
    {
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _provider.GetBlockByNumberAsync(1L, false, Arg.Any<CancellationToken>())
            .Returns(BlockWithTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(1L);

        var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 20);
        var count = 0;
        watcher.OnHealthChecked += (_, _) => Interlocked.Increment(ref count);

        await watcher.StartAsync();
        await Task.Delay(100);
        await watcher.StopAsync();
        var afterStop = count;

        await Task.Delay(150);
        Assert.Equal(afterStop, count);
    }

    [Fact]
    public async Task StartAsync_Twice_DoesNotDoubleStart()
    {
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _provider.GetBlockByNumberAsync(1L, false, Arg.Any<CancellationToken>())
            .Returns(BlockWithTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(1L);

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 50);
        await watcher.StartAsync();
        await watcher.StartAsync();
        await Task.Delay(120);
        await watcher.StopAsync();
    }

    [Fact]
    public async Task StopAsync_BeforeStart_IsNoOp()
    {
        var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 50);
        var ex = await Record.ExceptionAsync(() => watcher.StopAsync());
        Assert.Null(ex);
    }

    [Fact]
    public async Task DisposeAsync_CancelsGracefully()
    {
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _provider.GetBlockByNumberAsync(1L, false, Arg.Any<CancellationToken>())
            .Returns(BlockWithTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(1L);

        var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 20);
        await watcher.StartAsync();
        await Task.Delay(80);

        var disposeTask = watcher.DisposeAsync().AsTask();
        var finished = await Task.WhenAny(disposeTask, Task.Delay(2000));
        Assert.Same(disposeTask, finished);
    }

    [Fact]
    public async Task BlockAge_FutureBlockTimestamp_ClampedToZero()
    {
        _provider.GetBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _provider.GetBlockByNumberAsync(1L, false, Arg.Any<CancellationToken>())
            .Returns(BlockWithTimestamp(DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds()));
        _provider.GetChainIdAsync(Arg.Any<CancellationToken>()).Returns(1L);

        await using var watcher = new EvmNodeHealthWatcher(_provider, _network, intervalMs: 50);
        var tcs = new TaskCompletionSource<EvmNodeHealthReport>();
        watcher.OnHealthChecked += (_, e) => tcs.TrySetResult(e.Report);

        await watcher.StartAsync();
        var report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.Zero, report.BlockAge);
    }

    [Fact]
    public async Task Constructor_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new EvmNodeHealthWatcher(null!, _network));
    }

    [Fact]
    public async Task Constructor_NullNetwork_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new EvmNodeHealthWatcher(_provider, null!));
    }
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/ChainKit.Evm.Tests --nologo --filter "FullyQualifiedName~EvmNodeHealthWatcherTests"`

Expected: PASSED (14/14).

- [ ] **Step 3: Run full test suite**

Run: `dotnet test --nologo --filter "Category!=Integration&Category!=E2E"`

Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/ChainKit.Evm.Tests/Watching/EvmNodeHealthWatcherTests.cs
git commit -m "test: cover EVM health watcher resilience + lifecycle

Parallel to Tron coverage: provider-throw, continue-after-failure,
handler-throws, Stop idempotency, Start-twice, null-arg ctor guards,
and BlockAge clamping for future-dated timestamps.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: Add matching null-arg tests to Tron watcher

**Rationale:** EVM tests covered ctor-guards; add parity coverage for Tron.

**Files:**
- Modify: `tests/ChainKit.Tron.Tests/Watching/TronNodeHealthWatcherTests.cs` (append)

- [ ] **Step 1: Append ctor guard test**

```csharp
    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TronNodeHealthWatcher(null!));
    }
```

- [ ] **Step 2: Run test + full suite**

Run: `dotnet test --nologo --filter "Category!=Integration&Category!=E2E"`

Expected: all tests pass.

- [ ] **Step 3: Commit**

```bash
git add tests/ChainKit.Tron.Tests/Watching/TronNodeHealthWatcherTests.cs
git commit -m "test: add null-provider ctor guard test for TronNodeHealthWatcher

Parity with EVM watcher's constructor null-arg coverage.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 10: Update CLAUDE.md convention list

**Rationale:** CLAUDE.md lists IAsyncDisposable classes. Add the two new watchers so future contributors know they exist.

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Find the IAsyncDisposable line in CLAUDE.md**

Run: `grep -n "IAsyncDisposable" CLAUDE.md`

Expected: one or two lines listing existing `IAsyncDisposable` classes.

- [ ] **Step 2: Extend the list**

Edit the matching line so the watchers appear. The original line reads approximately:

```
- IAsyncDisposable：TronTransactionWatcher、EvmTransactionWatcher
```

Change to:

```
- IAsyncDisposable：TronTransactionWatcher、EvmTransactionWatcher、TronNodeHealthWatcher、EvmNodeHealthWatcher
```

(If the exact wording differs — e.g. two separate lines for Tron vs EVM — extend each line accordingly.)

- [ ] **Step 3: Add a short note under the 關鍵設計決策 section**

Find the section marked `## 關鍵設計決策` and append a bullet:

```
- 節點健康檢查：`TronNodeHealthWatcher` / `EvmNodeHealthWatcher` 以 event-based poll 模式回報 raw metrics（Reachable/Latency/BlockNumber/BlockAge，EVM 另含 ChainIdMatch），由呼叫端自行定義健康 threshold
```

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: note NodeHealthWatcher classes in CLAUDE.md conventions

Adds the new TronNodeHealthWatcher and EvmNodeHealthWatcher to the
IAsyncDisposable convention list and a summary line under the key
design decisions section.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review

### Spec coverage

| Spec requirement | Task(s) covering it |
| --- | --- |
| Per-chain watcher classes + symmetric API | Tasks 2, 6 |
| Configurable polling interval | Tasks 2, 6 (ctor param verified in happy-path tests) |
| `OnHealthChecked` every poll, raw metrics | Tasks 2, 6 |
| `IAsyncDisposable` lifecycle | Tasks 4 (Tron), 8 (EVM) |
| Report: Reachable, Latency, BlockNumber, BlockAge | Tasks 2, 5 (Tron), 6, 8 (EVM) |
| Report: ChainIdMatch (EVM only) | Task 7 |
| Error handling: provider-throw → Reachable=false + Error | Tasks 3, 8 |
| Error handling: polling continues after failure | Tasks 3, 8 |
| Error handling: handler-throws swallowed + logged | Tasks 3, 8 |
| Idempotency: Start-twice, Stop-before-Start, Stop-idempotent | Tasks 4, 8 |
| DisposeAsync cancels gracefully | Tasks 4, 8 |
| BlockAge clamped non-negative | Tasks 5, 8 |
| ChainId cached after first success | Task 7 |
| Prerequisite `IEvmProvider.GetChainIdAsync` | Task 1 |

All spec requirements mapped to tasks. No gaps.

### Placeholder scan

No "TBD", "TODO", "fill in", or "similar to Task N" in this plan. Each code step shows the exact code. Each commit step shows the exact commit message.

### Type consistency

- `TronNodeHealthReport` fields used in Task 2 (happy path), Task 3 (failure), Task 5 (BlockAge): Timestamp, Reachable, Latency, BlockNumber, BlockAge, Error. Consistent.
- `EvmNodeHealthReport` adds `ChainIdMatch`. Used in Tasks 6, 7, 8. Consistent.
- `StartAsync` / `StopAsync` / `DisposeAsync` names match across Tron + EVM and match existing `TronTransactionWatcher` pattern.
- `OnHealthChecked` event name consistent across all tasks and both chains.
- `intervalMs: int` ctor param consistent with `PollingBlockStream` style.

All identifiers consistent across tasks.

### Risks / caveats for the executor

- **Timing-sensitive tests** (Tasks 3-8): if running on a heavily loaded CI machine, assertions like "at least 2 fires in 200ms with 30ms interval" may need loosening. Prefer bumping delays over reducing interval.
- **`BlockInfo` / `EvmNetworkConfig` shape**: construction in tests assumes ctor shape shown in Step 2 of Task 2 and Task 6. If the real type differs, adapt to match (the tests note this inline).
- **`EvmHttpProvider` RPC helper name**: Task 1 Step 5 assumes `RpcAsync`; the inline note tells the executor to verify by reading one existing method.
