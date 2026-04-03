# Plan 3: Transaction Watching Implementation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build transaction monitoring — ITronBlockStream abstraction with ZMQ and Polling implementations, plus TronTransactionWatcher for multi-address event-driven monitoring.

**Architecture:** `ChainKit.Tron/Watching/` namespace. ITronBlockStream provides blocks via IAsyncEnumerable. Two implementations: PollingBlockStream (uses ITronProvider) and ZmqBlockStream (uses NetMQ). TronTransactionWatcher consumes a stream, filters by watched addresses, and fires events.

**Tech Stack:** .NET 10, C#, NetMQ 4.0.2.2, xUnit, NSubstitute

**Spec:** `docs/superpowers/specs/2026-04-03-tron-sdk-design.md`

**Depends on:** Plan 1 + Plan 2 — completed (141 tests passing).

---

### Task 1: Watcher Models + ITronBlockStream

**Files:**
- Create: `src/ChainKit.Tron/Watching/ITronBlockStream.cs`
- Create: `src/ChainKit.Tron/Models/WatcherModels.cs`

- [ ] **Step 1: Create watcher models**

```csharp
// src/ChainKit.Tron/Models/WatcherModels.cs
namespace ChainKit.Tron.Models;

public record TronBlock(
    long BlockNumber, string BlockId,
    DateTimeOffset Timestamp,
    IReadOnlyList<TronBlockTransaction> Transactions);

public record TronBlockTransaction(
    string TxId, string FromAddress, string ToAddress,
    string ContractType, byte[] RawData);

public record TrxReceivedEventArgs(
    string TxId, string FromAddress, string ToAddress,
    decimal Amount, long BlockNumber, DateTimeOffset Timestamp);

public record Trc20ReceivedEventArgs(
    string TxId, string FromAddress, string ToAddress,
    string ContractAddress, string Symbol, decimal Amount,
    long BlockNumber, DateTimeOffset Timestamp);

public record TransactionConfirmedEventArgs(
    string TxId, long BlockNumber, bool Success);
```

- [ ] **Step 2: Create ITronBlockStream interface**

```csharp
// src/ChainKit.Tron/Watching/ITronBlockStream.cs
namespace ChainKit.Tron.Watching;

public interface ITronBlockStream
{
    IAsyncEnumerable<ChainKit.Tron.Models.TronBlock> StreamBlocksAsync(
        CancellationToken ct = default);
}
```

- [ ] **Step 3: Verify build**
- [ ] **Step 4: Commit** "feat(tron): add watcher models and ITronBlockStream interface"

---

### Task 2: PollingBlockStream

**Files:**
- Create: `src/ChainKit.Tron/Watching/PollingBlockStream.cs`
- Create: `tests/ChainKit.Tron.Tests/Watching/PollingBlockStreamTests.cs`

- [ ] **Step 1: Write tests**

Test using mocked ITronProvider. Verify:
- Polls GetNowBlockAsync at interval
- Returns new blocks only (skips already-seen block numbers)
- Respects CancellationToken
- Handles provider errors gracefully (retry on next interval)

- [ ] **Step 2: Implement PollingBlockStream**

Polls ITronProvider.GetNowBlockAsync() at configurable interval. Tracks last seen block number to avoid duplicates. Yields new blocks as IAsyncEnumerable.

- [ ] **Step 3: Run tests**
- [ ] **Step 4: Commit** "feat(tron): add PollingBlockStream for API-based block monitoring"

---

### Task 3: ZmqBlockStream

**Files:**
- Create: `src/ChainKit.Tron/Watching/ZmqBlockStream.cs`
- Create: `tests/ChainKit.Tron.Tests/Watching/ZmqBlockStreamTests.cs`
- Modify: `src/ChainKit.Tron/ChainKit.Tron.csproj` (add NetMQ package)

- [ ] **Step 1: Add NetMQ package**

Add `<PackageReference Include="NetMQ" Version="4.0.2.2" />` to ChainKit.Tron.csproj.

- [ ] **Step 2: Write tests**

ZMQ is hard to unit test (needs real socket). Write:
- Constructor validation tests
- Basic structure tests
- Integration test marked `[Trait("Category", "Integration")]`

- [ ] **Step 3: Implement ZmqBlockStream**

Subscribes to Tron node ZMQ endpoint. Parses incoming block data from ZMQ messages into TronBlock records.

- [ ] **Step 4: Run tests**
- [ ] **Step 5: Commit** "feat(tron): add ZmqBlockStream for ZMQ-based block monitoring"

---

### Task 4: TronTransactionWatcher

**Files:**
- Create: `src/ChainKit.Tron/Watching/TronTransactionWatcher.cs`
- Create: `tests/ChainKit.Tron.Tests/Watching/TronTransactionWatcherTests.cs`

- [ ] **Step 1: Write tests**

Use a mock ITronBlockStream that yields pre-built TronBlock records:
- WatchAddress adds address to watch set
- OnTrxReceived fires when watched address receives TRX
- OnTrc20Received fires when watched address receives TRC20
- UnwatchAddress stops notifications for that address
- Dynamic add/remove during watching
- Handles thousands of addresses efficiently

- [ ] **Step 2: Implement TronTransactionWatcher**

```csharp
public class TronTransactionWatcher : IAsyncDisposable
{
    private readonly ITronBlockStream _stream;
    private readonly HashSet<string> _watchedAddresses = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;
    private Task? _watchTask;

    public TronTransactionWatcher(ITronBlockStream stream) { _stream = stream; }

    public void WatchAddress(string address) { ... } // normalize + add to hashset
    public void WatchAddresses(IEnumerable<string> addresses) { ... }
    public void UnwatchAddress(string address) { ... }

    public event EventHandler<TrxReceivedEventArgs>? OnTrxReceived;
    public event EventHandler<Trc20ReceivedEventArgs>? OnTrc20Received;
    public event EventHandler<TransactionConfirmedEventArgs>? OnTransactionConfirmed;

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _watchTask = WatchLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_watchTask != null) await _watchTask;
    }

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        await foreach (var block in _stream.StreamBlocksAsync(ct))
        {
            foreach (var tx in block.Transactions)
            {
                if (_watchedAddresses.Contains(tx.ToAddress))
                    ProcessTransaction(tx, block);
            }
        }
    }

    // Parse transaction type and fire appropriate event
    private void ProcessTransaction(TronBlockTransaction tx, TronBlock block) { ... }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }
}
```

- [ ] **Step 3: Run ALL tests**
- [ ] **Step 4: Commit** "feat(tron): add TronTransactionWatcher for multi-address monitoring"

---

## Plan 3 Complete

After all 4 tasks:
- **ITronBlockStream** interface
- **PollingBlockStream** — polls API at interval
- **ZmqBlockStream** — subscribes to ZMQ
- **TronTransactionWatcher** — multi-address event-driven monitoring
- All watcher DTOs and event args
