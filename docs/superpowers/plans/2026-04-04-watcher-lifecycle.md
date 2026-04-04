# Watcher Lifecycle Enhancement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enhance `TronTransactionWatcher` to support bidirectional monitoring (incoming + outgoing) with three-stage transaction lifecycle (Unconfirmed → Confirmed / Failed).

**Architecture:** Watcher discovers transactions from blocks (via `ITronBlockStream`) and tracks pending confirmations internally via a background loop that polls the Solidity Node (via `ITronProvider`). Six events in two layers: four discovery events (TRX/TRC20 × Received/Sent) fire at Unconfirmed, two final-state events (Confirmed/Failed) fire after Solidity Node confirmation.

**Tech Stack:** .NET 10, C#, xUnit, NSubstitute

**Spec:** `docs/superpowers/specs/2026-04-04-watcher-lifecycle-design.md`

---

## File Structure

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `src/ChainKit.Tron/Models/WatcherModels.cs` | Add `TrxSentEventArgs`, `Trc20SentEventArgs`, `TransactionFailedEventArgs`, `TransactionFailureReason` enum; update `TransactionConfirmedEventArgs` |
| Modify | `src/ChainKit.Tron/Models/AccountModels.cs` | Add `ReceiptResult` field to `TransactionInfoDto` |
| Modify | `src/ChainKit.Tron/Providers/TronHttpProvider.cs` | Parse `receipt.result` into `ReceiptResult` |
| Modify | `src/ChainKit.Tron/Providers/TronGrpcProvider.cs` | Parse receipt field 7 into `ReceiptResult` |
| Modify | `src/ChainKit.Tron/Watching/TronTransactionWatcher.cs` | Add outgoing events, confirmation tracker, failure detection |
| Modify | `tests/ChainKit.Tron.Tests/Watching/TronTransactionWatcherTests.cs` | Update existing tests, add new tests |

---

### Task 1: Update Data Models

**Files:**
- Modify: `src/ChainKit.Tron/Models/WatcherModels.cs`
- Modify: `src/ChainKit.Tron/Models/AccountModels.cs`

- [ ] **Step 1: Add new types to WatcherModels.cs**

Replace the entire file content with:

```csharp
namespace ChainKit.Tron.Models;

public record TronBlock(
    long BlockNumber, string BlockId,
    DateTimeOffset Timestamp,
    IReadOnlyList<TronBlockTransaction> Transactions);

public record TronBlockTransaction(
    string TxId, string FromAddress, string ToAddress,
    string ContractType, byte[] RawData);

// --- Discovery events (fire once at Unconfirmed) ---

public record TrxReceivedEventArgs(
    string TxId, string FromAddress, string ToAddress,
    decimal Amount, long BlockNumber, DateTimeOffset Timestamp);

public record TrxSentEventArgs(
    string TxId, string FromAddress, string ToAddress,
    decimal Amount, long BlockNumber, DateTimeOffset Timestamp);

public record Trc20ReceivedEventArgs(
    string TxId, string FromAddress, string ToAddress,
    string ContractAddress, string Symbol,
    decimal RawAmount,
    decimal? Amount,
    int Decimals,
    long BlockNumber, DateTimeOffset Timestamp);

public record Trc20SentEventArgs(
    string TxId, string FromAddress, string ToAddress,
    string ContractAddress, string Symbol,
    decimal RawAmount,
    decimal? Amount,
    int Decimals,
    long BlockNumber, DateTimeOffset Timestamp);

// --- Final-state events ---

public record TransactionConfirmedEventArgs(
    string TxId, long BlockNumber, DateTimeOffset Timestamp);

public record TransactionFailedEventArgs(
    string TxId, long BlockNumber,
    TransactionFailureReason Reason, string? Message);

/// <summary>
/// Failure reasons for watcher transaction lifecycle events.
/// </summary>
public enum TransactionFailureReason
{
    /// <summary>Contract execution reverted (e.g. insufficient token balance).</summary>
    ContractReverted,
    /// <summary>Not enough Energy to complete contract execution.</summary>
    OutOfEnergy,
    /// <summary>Not enough Bandwidth for the transaction.</summary>
    OutOfBandwidth,
    /// <summary>Solidity Node did not confirm within the maximum pending age.</summary>
    Expired,
    /// <summary>Other or unknown failure.</summary>
    Other
}
```

- [ ] **Step 2: Add ReceiptResult to TransactionInfoDto**

In `src/ChainKit.Tron/Models/AccountModels.cs`, add `ReceiptResult` parameter at the end of `TransactionInfoDto`:

```csharp
public record TransactionInfoDto(
    string TxId, long BlockNumber, long BlockTimestamp, string ContractResult, long Fee, long EnergyUsage, long NetUsage,
    // Contract detail fields (populated by GetTransactionByIdAsync)
    string ContractType = "", string OwnerAddress = "", string ToAddress = "",
    long AmountSun = 0, string? ContractAddress = null, string? ContractData = null,
    // Resource TRX costs in Sun (populated by GetTransactionInfoByIdAsync from receipt)
    long EnergyFee = 0, long NetFee = 0,
    // Contract execution result from receipt (e.g. "SUCCESS", "REVERT", "OUT_OF_ENERGY")
    string ReceiptResult = "");
```

- [ ] **Step 3: Verify build**

Run: `dotnet build`
Expected: Build succeeds. `TransactionConfirmedEventArgs` constructor change causes errors in watcher and tests — that's expected, we fix in Task 2.

- [ ] **Step 4: Commit**

```bash
git add src/ChainKit.Tron/Models/WatcherModels.cs src/ChainKit.Tron/Models/AccountModels.cs
git commit -m "feat(models): add watcher lifecycle event types and ReceiptResult field"
```

---

### Task 2: Parse ReceiptResult in Providers

**Files:**
- Modify: `src/ChainKit.Tron/Providers/TronHttpProvider.cs:595-621`
- Modify: `src/ChainKit.Tron/Providers/TronGrpcProvider.cs:650-686`

- [ ] **Step 1: Update HTTP provider ParseTransactionInfo**

In `TronHttpProvider.cs`, method `ParseTransactionInfo` (line ~595), add `receiptResult` parsing inside the existing receipt block and pass it to the constructor:

```csharp
    private static TransactionInfoDto ParseTransactionInfo(string json, string txId)
    {
        var root = JsonDocument.Parse(json).RootElement;

        var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? txId : txId;
        var blockNum = root.TryGetProperty("blockNumber", out var bnEl) ? bnEl.GetInt64() : 0;
        var blockTs = root.TryGetProperty("blockTimeStamp", out var btsEl) ? btsEl.GetInt64() : 0;
        var fee = root.TryGetProperty("fee", out var feeEl) ? feeEl.GetInt64() : 0;
        long energy = 0, net = 0, energyFee = 0, netFee = 0;
        var receiptResult = "";
        if (root.TryGetProperty("receipt", out var rcEl))
        {
            receiptResult = rcEl.TryGetProperty("result", out var rrEl) ? rrEl.GetString() ?? "" : "";
            energy = rcEl.TryGetProperty("energy_usage_total", out var euEl) ? euEl.GetInt64() : 0;
            net = rcEl.TryGetProperty("net_usage", out var nuEl) ? nuEl.GetInt64() : 0;
            energyFee = rcEl.TryGetProperty("energy_fee", out var efEl) ? efEl.GetInt64() : 0;
            netFee = rcEl.TryGetProperty("net_fee", out var nfEl) ? nfEl.GetInt64() : 0;
        }
        var contractResult = root.TryGetProperty("contractResult", out var crEl)
            && crEl.GetArrayLength() > 0
                ? crEl[0].GetString() ?? "" : "";

        // Parse contract_address for deploy transactions
        var contractAddress = root.TryGetProperty("contract_address", out var caEl)
            ? caEl.GetString() : null;

        return new TransactionInfoDto(id, blockNum, blockTs, contractResult, fee, energy, net,
            ContractAddress: contractAddress, EnergyFee: energyFee, NetFee: netFee,
            ReceiptResult: receiptResult);
    }
```

- [ ] **Step 2: Update gRPC provider ParseTransactionInfo**

In `TronGrpcProvider.cs`, method `ParseTransactionInfo` (line ~650), parse receipt field 7 (the `contractResult` enum from the `ResourceReceipt` protobuf message):

```csharp
    private static TransactionInfoDto ParseTransactionInfo(byte[] data, string txId)
    {
        if (data.Length == 0)
            return new TransactionInfoDto(txId, 0, 0, "", 0, 0, 0);

        var idBytes = ParseBytesField(data, 1);
        var id = idBytes.Length > 0 ? Convert.ToHexString(idBytes).ToLowerInvariant() : txId;
        var fee = ParseVarintField(data, 2);
        var blockNum = ParseVarintField(data, 3);
        var blockTs = ParseVarintField(data, 4);

        // ResourceReceipt at field 8
        var receiptBytes = ParseBytesField(data, 8);
        long energyUsage = 0;
        long netUsage = 0;
        long energyFee = 0;
        long netFee = 0;
        var receiptResult = "";
        if (receiptBytes.Length > 0)
        {
            energyUsage = ParseVarintField(receiptBytes, 1);
            netUsage = ParseVarintField(receiptBytes, 4);
            energyFee = ParseVarintField(receiptBytes, 2);
            netFee = ParseVarintField(receiptBytes, 6);
            // Field 7 = contractResult enum (varint)
            var resultCode = (int)ParseVarintField(receiptBytes, 7);
            receiptResult = resultCode switch
            {
                0 => "DEFAULT",
                1 => "SUCCESS",
                2 => "REVERT",
                10 => "OUT_OF_ENERGY",
                11 => "OUT_OF_TIME",
                14 => "TRANSFER_FAILED",
                _ => resultCode > 1 ? "FAILED" : ""
            };
        }

        var contractResults = ParseRepeatedBytesField(data, 9);
        var contractResult = contractResults.Count > 0
            ? Convert.ToHexString(contractResults[0]).ToLowerInvariant()
            : "";

        return new TransactionInfoDto(id, blockNum, blockTs, contractResult, fee, energyUsage, netUsage,
            EnergyFee: energyFee, NetFee: netFee, ReceiptResult: receiptResult);
    }
```

- [ ] **Step 3: Verify build**

Run: `dotnet build`
Expected: Build still has errors from `TransactionConfirmedEventArgs` change — fixed in Task 3.

- [ ] **Step 4: Commit**

```bash
git add src/ChainKit.Tron/Providers/TronHttpProvider.cs src/ChainKit.Tron/Providers/TronGrpcProvider.cs
git commit -m "feat(providers): parse ReceiptResult from Solidity Node responses"
```

---

### Task 3: Fix Breaking Changes and Update Existing Tests

**Files:**
- Modify: `src/ChainKit.Tron/Watching/TronTransactionWatcher.cs:10-27,50-53,89-162,306-310`
- Modify: `tests/ChainKit.Tron.Tests/Watching/TronTransactionWatcherTests.cs`

This task updates the watcher constructor (provider required), replaces the old `OnTransactionConfirmed` fire-on-block behavior, and fixes all existing tests to compile and pass.

- [ ] **Step 1: Update watcher constructor and event declarations**

In `TronTransactionWatcher.cs`, change the constructor and events:

```csharp
public class TronTransactionWatcher : IAsyncDisposable
{
    private readonly ITronBlockStream _stream;
    private readonly ITronProvider _provider;
    private readonly TokenInfoCache _tokenCache = new();
    private readonly HashSet<string> _watchedAddresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _watchTask;

    // TRC20 transfer(address,uint256) selector: a9059cbb
    private static readonly byte[] Trc20TransferSelector = { 0xa9, 0x05, 0x9c, 0xbb };

    /// <summary>
    /// Creates a new watcher.
    /// </summary>
    /// <param name="stream">Block source (polling or ZMQ).</param>
    /// <param name="provider">Provider for Solidity Node confirmation queries.</param>
    public TronTransactionWatcher(ITronBlockStream stream, ITronProvider provider)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }
```

Update events — remove old `OnTransactionConfirmed`, add new events:

```csharp
    /// <summary>Fires when a TRX transfer TO a watched address is found in a block.</summary>
    public event EventHandler<TrxReceivedEventArgs>? OnTrxReceived;
    /// <summary>Fires when a TRX transfer FROM a watched address is found in a block.</summary>
    public event EventHandler<TrxSentEventArgs>? OnTrxSent;
    /// <summary>Fires when a TRC20 transfer TO a watched address is found in a block.</summary>
    public event EventHandler<Trc20ReceivedEventArgs>? OnTrc20Received;
    /// <summary>Fires when a TRC20 transfer FROM a watched address is found in a block.</summary>
    public event EventHandler<Trc20SentEventArgs>? OnTrc20Sent;
    /// <summary>Fires when a pending transaction is confirmed by Solidity Node.</summary>
    public event EventHandler<TransactionConfirmedEventArgs>? OnTransactionConfirmed;
    /// <summary>Fires when a pending transaction fails or times out.</summary>
    public event EventHandler<TransactionFailedEventArgs>? OnTransactionFailed;
```

- [ ] **Step 2: Remove old OnTransactionConfirmed invocation from ProcessTransactionAsync**

In `ProcessTransactionAsync`, remove the last 3 lines that fire `OnTransactionConfirmed`:

```csharp
        // DELETE these lines (they were at the end of ProcessTransactionAsync):
        // Always fire confirmed event for matched transactions
        // OnTransactionConfirmed?.Invoke(this, new TransactionConfirmedEventArgs(
        //     tx.TxId, block.BlockNumber, true));
```

The confirmation event will be fired by the confirmation tracker in Task 5.

- [ ] **Step 3: Update existing tests for constructor change**

In `TronTransactionWatcherTests.cs`, every test that creates a watcher needs a provider. Add a helper method and update all test constructors:

```csharp
public class TronTransactionWatcherTests
{
    // ... existing constants ...

    private static ITronProvider MockProvider() => Substitute.For<ITronProvider>();

    // ... existing helper methods ...
```

Update each test. For every `new TronTransactionWatcher(stream)` → `new TronTransactionWatcher(stream, MockProvider())`.

Tests to update:
- `Constructor_NullStream_Throws` → also add `Constructor_NullProvider_Throws`
- `WatchAddress_TrxReceived_FiresEvent` → add `MockProvider()`
- `WatchAddress_Trc20Received_FiresEvent` → add `MockProvider()`
- `UnwatchAddress_StopsEvents` → add `MockProvider()`
- `WatchAddresses_MultipleAddresses_FiresForBoth` → add `MockProvider()`
- `UnrelatedTransaction_DoesNotFireEvents` → add `MockProvider()`, remove `OnTransactionConfirmed` subscription
- `StartAsync_StopAsync_Lifecycle` → add `MockProvider()`
- `DisposeAsync_CleansUp` → add `MockProvider()`
- `TrxReceived_WithRealAmount_EventContainsAmount` → add `MockProvider()`
- `Trc20Received_WithRealData_EventContainsAmountAndContract` → add `MockProvider()`
- `Trc20Received_WithoutProvider_KnownToken_ConvertsAmount` → remove test (provider required now)
- `Trc20Received_WithoutProvider_UnknownToken_ReturnsRawAmount` → remove test (provider required now)

Updated test examples:

```csharp
    [Fact]
    public void Constructor_NullStream_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TronTransactionWatcher(null!, MockProvider()));
    }

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TronTransactionWatcher(new MockBlockStream(), null!));
    }

    [Fact]
    public async Task WatchAddress_TrxReceived_FiresEvent()
    {
        var block = MakeBlock(1, MakeTrxTx(OtherAddr, WatchedAddr));
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream, MockProvider());

        TrxReceivedEventArgs? received = null;
        watcher.OnTrxReceived += (_, e) => received = e;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.NotNull(received);
        Assert.Equal("tx1", received!.TxId);
        Assert.Equal(OtherAddr, received.FromAddress);
        Assert.Equal(WatchedAddr, received.ToAddress);
        Assert.Equal(1, received.BlockNumber);
    }

    [Fact]
    public async Task UnrelatedTransaction_DoesNotFireEvents()
    {
        var block = MakeBlock(1, MakeTrxTx(UnrelatedAddr, UnrelatedAddr));
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream, MockProvider());

        int fireCount = 0;
        watcher.OnTrxReceived += (_, _) => fireCount++;
        watcher.OnTrc20Received += (_, _) => fireCount++;
        watcher.OnTrxSent += (_, _) => fireCount++;
        watcher.OnTrc20Sent += (_, _) => fireCount++;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.Equal(0, fireCount);
    }
```

Remove `OnTransactionConfirmed_FiresForAllMatchedTransactions` — this behavior is replaced by the confirmation tracker (Task 5).

Remove `FromAddress_AlsoMatchesWatchedAddress` — replaced by the outgoing event tests (Task 4).

- [ ] **Step 4: Run tests**

Run: `dotnet test --filter "Category!=Integration"`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/ChainKit.Tron/Watching/TronTransactionWatcher.cs tests/ChainKit.Tron.Tests/Watching/TronTransactionWatcherTests.cs
git commit -m "refactor(watcher): require provider, update event signatures

BREAKING CHANGE: ITronProvider is now required. TransactionConfirmedEventArgs
removes Success field, adds Timestamp. OnTransactionConfirmed now fires from
Solidity Node confirmation, not block inclusion."
```

---

### Task 4: Add Outgoing Events (TDD)

**Files:**
- Test: `tests/ChainKit.Tron.Tests/Watching/TronTransactionWatcherTests.cs`
- Modify: `src/ChainKit.Tron/Watching/TronTransactionWatcher.cs:89-162`

- [ ] **Step 1: Write failing tests for outgoing TRX**

```csharp
    [Fact]
    public async Task FromAddress_TrxSent_FiresEvent()
    {
        var block = MakeBlock(1, MakeTrxTxWithAmount(WatchedAddr, UnrelatedAddr, 5_000_000));
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream, MockProvider());

        TrxSentEventArgs? sent = null;
        watcher.OnTrxSent += (_, e) => sent = e;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.NotNull(sent);
        Assert.Equal("tx1", sent!.TxId);
        Assert.Equal(WatchedAddr, sent.FromAddress);
        Assert.Equal(UnrelatedAddr, sent.ToAddress);
        Assert.Equal(5m, sent.Amount);
    }

    [Fact]
    public async Task FromAddress_TrxSent_DoesNotFireReceived()
    {
        var block = MakeBlock(1, MakeTrxTxWithAmount(WatchedAddr, UnrelatedAddr, 5_000_000));
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream, MockProvider());

        TrxReceivedEventArgs? received = null;
        watcher.OnTrxReceived += (_, e) => received = e;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.Null(received);
    }
```

- [ ] **Step 2: Run tests to verify failure**

Run: `dotnet test --filter "FromAddress_TrxSent" --no-build`
Expected: FAIL — `OnTrxSent` never fires.

- [ ] **Step 3: Write failing tests for outgoing TRC20**

```csharp
    [Fact]
    public async Task FromAddress_Trc20Sent_FiresEvent()
    {
        var contractAddr = "41" + new string('e', 40);
        long tokenAmount = 500_000_000;
        var tx = MakeTrc20TxWithData(WatchedAddr, contractAddr, UnrelatedAddr, tokenAmount, "trc20out");
        var block = MakeBlock(1, tx);
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream, MockProvider());

        Trc20SentEventArgs? sent = null;
        watcher.OnTrc20Sent += (_, e) => sent = e;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.NotNull(sent);
        Assert.Equal("trc20out", sent!.TxId);
        Assert.Equal(WatchedAddr, sent.FromAddress);
        Assert.Equal(contractAddr, sent.ContractAddress);
        Assert.Equal((decimal)tokenAmount, sent.RawAmount);
    }
```

- [ ] **Step 4: Write failing test for self-transfer**

```csharp
    [Fact]
    public async Task SelfTransfer_FiresBothReceivedAndSent()
    {
        var block = MakeBlock(1, MakeTrxTxWithAmount(WatchedAddr, WatchedAddr, 1_000_000));
        var stream = new MockBlockStream(block);
        await using var watcher = new TronTransactionWatcher(stream, MockProvider());

        TrxReceivedEventArgs? received = null;
        TrxSentEventArgs? sent = null;
        watcher.OnTrxReceived += (_, e) => received = e;
        watcher.OnTrxSent += (_, e) => sent = e;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(200);

        Assert.NotNull(received);
        Assert.NotNull(sent);
        Assert.Equal("tx1", received!.TxId);
        Assert.Equal("tx1", sent!.TxId);
    }
```

- [ ] **Step 5: Implement outgoing events in ProcessTransactionAsync**

Replace `ProcessTransactionAsync` in `TronTransactionWatcher.cs`:

```csharp
    private async Task ProcessTransactionAsync(TronBlockTransaction tx, TronBlock block, CancellationToken ct)
    {
        bool fromWatched, toWatched;
        lock (_lock)
        {
            fromWatched = _watchedAddresses.Contains(tx.FromAddress);
            toWatched = _watchedAddresses.Contains(tx.ToAddress);
        }

        if (tx.ContractType == "TransferContract")
        {
            var amount = ParseTrxAmount(tx.RawData);

            if (toWatched)
            {
                OnTrxReceived?.Invoke(this, new TrxReceivedEventArgs(
                    tx.TxId, tx.FromAddress, tx.ToAddress,
                    amount, block.BlockNumber, block.Timestamp));
            }
            if (fromWatched)
            {
                OnTrxSent?.Invoke(this, new TrxSentEventArgs(
                    tx.TxId, tx.FromAddress, tx.ToAddress,
                    amount, block.BlockNumber, block.Timestamp));
            }
        }
        else if (tx.ContractType == "TriggerSmartContract")
        {
            var trc20Info = ParseTrc20Transfer(tx.RawData);
            var effectiveTo = trc20Info.Recipient ?? tx.ToAddress;

            // Check if the effective recipient is watched (for Received)
            bool effectiveToWatched;
            lock (_lock)
            {
                effectiveToWatched = _watchedAddresses.Contains(effectiveTo)
                                  || _watchedAddresses.Contains(tx.ToAddress);
            }

            // Resolve token info (needed for both Received and Sent)
            string symbol = "";
            decimal rawAmount = trc20Info.Amount;
            decimal? convertedAmount = null;
            int decimals = 0;

            if (!string.IsNullOrEmpty(trc20Info.ContractAddress))
            {
                try
                {
                    var tokenInfo = await _tokenCache.GetOrResolveAsync(
                        trc20Info.ContractAddress, _provider, ct);
                    symbol = tokenInfo.Symbol;
                    decimals = tokenInfo.Decimals;
                    if (tokenInfo.Decimals > 0)
                        convertedAmount = trc20Info.Amount / (decimal)Math.Pow(10, tokenInfo.Decimals);
                }
                catch
                {
                    // resolution failed — try known-token cache only
                    var tokenInfo = _tokenCache.Get(trc20Info.ContractAddress);
                    if (tokenInfo != null)
                    {
                        symbol = tokenInfo.Symbol;
                        decimals = tokenInfo.Decimals;
                        if (tokenInfo.Decimals > 0)
                            convertedAmount = trc20Info.Amount / (decimal)Math.Pow(10, tokenInfo.Decimals);
                    }
                }
            }

            if (effectiveToWatched)
            {
                OnTrc20Received?.Invoke(this, new Trc20ReceivedEventArgs(
                    tx.TxId, tx.FromAddress, effectiveTo,
                    trc20Info.ContractAddress, symbol,
                    rawAmount, convertedAmount, decimals,
                    block.BlockNumber, block.Timestamp));
            }
            if (fromWatched)
            {
                OnTrc20Sent?.Invoke(this, new Trc20SentEventArgs(
                    tx.TxId, tx.FromAddress, effectiveTo,
                    trc20Info.ContractAddress, symbol,
                    rawAmount, convertedAmount, decimals,
                    block.BlockNumber, block.Timestamp));
            }
        }

        // Pending tracking is added in Task 5 when the confirmation tracker fields exist.
    }
```

- [ ] **Step 6: Run tests**

Run: `dotnet test --filter "Category!=Integration"`
Expected: All tests pass including new outgoing event tests.

- [ ] **Step 7: Commit**

```bash
git add src/ChainKit.Tron/Watching/TronTransactionWatcher.cs tests/ChainKit.Tron.Tests/Watching/TronTransactionWatcherTests.cs
git commit -m "feat(watcher): add OnTrxSent and OnTrc20Sent events for outgoing transactions"
```

---

### Task 5: Add Confirmation Tracker — Confirmed Flow (TDD)

**Files:**
- Test: `tests/ChainKit.Tron.Tests/Watching/TronTransactionWatcherTests.cs`
- Modify: `src/ChainKit.Tron/Watching/TronTransactionWatcher.cs`

- [ ] **Step 1: Write failing test for TRX confirmation**

```csharp
    [Fact]
    public async Task ConfirmationTracker_TrxConfirmed_FiresEvent()
    {
        var provider = Substitute.For<ITronProvider>();
        var tx = MakeTrxTxWithAmount(OtherAddr, WatchedAddr, 10_000_000);
        var block = MakeBlock(1, tx);
        var stream = new MockBlockStream(block);

        // Solidity Node confirms immediately
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        provider.GetTransactionInfoByIdAsync("tx1", Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto("tx1", 1, ts, "", 0, 0, 0, ReceiptResult: "SUCCESS"));

        await using var watcher = new TronTransactionWatcher(stream, provider,
            confirmationIntervalMs: 50);

        var tcs = new TaskCompletionSource<TransactionConfirmedEventArgs>();
        watcher.OnTransactionConfirmed += (_, e) => tcs.TrySetResult(e);

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();

        var confirmed = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("tx1", confirmed.TxId);
        Assert.Equal(1, confirmed.BlockNumber);
    }
```

- [ ] **Step 2: Write failing test for delayed confirmation**

```csharp
    [Fact]
    public async Task ConfirmationTracker_DelayedConfirmation_EventuallyFires()
    {
        var provider = Substitute.For<ITronProvider>();
        var tx = MakeTrxTxWithAmount(OtherAddr, WatchedAddr, 10_000_000);
        var block = MakeBlock(1, tx);
        var stream = new MockBlockStream(block);

        // First 2 calls: not yet on Solidity Node. Then: confirmed.
        int callCount = 0;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        provider.GetTransactionInfoByIdAsync("tx1", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref callCount) <= 2)
                    return Task.FromResult(new TransactionInfoDto("", 0, 0, "", 0, 0, 0));
                return Task.FromResult(new TransactionInfoDto("tx1", 1, ts, "", 0, 0, 0,
                    ReceiptResult: "SUCCESS"));
            });

        await using var watcher = new TronTransactionWatcher(stream, provider,
            confirmationIntervalMs: 50);

        var tcs = new TaskCompletionSource<TransactionConfirmedEventArgs>();
        watcher.OnTransactionConfirmed += (_, e) => tcs.TrySetResult(e);

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();

        var confirmed = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("tx1", confirmed.TxId);
        Assert.True(callCount >= 3);
    }
```

- [ ] **Step 3: Write failing test — self-transfer confirms only once**

```csharp
    [Fact]
    public async Task ConfirmationTracker_SelfTransfer_ConfirmsOnce()
    {
        var provider = Substitute.For<ITronProvider>();
        var tx = MakeTrxTxWithAmount(WatchedAddr, WatchedAddr, 1_000_000);
        var block = MakeBlock(1, tx);
        var stream = new MockBlockStream(block);

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        provider.GetTransactionInfoByIdAsync("tx1", Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto("tx1", 1, ts, "", 0, 0, 0, ReceiptResult: "SUCCESS"));

        await using var watcher = new TronTransactionWatcher(stream, provider,
            confirmationIntervalMs: 50);

        var confirmedCount = 0;
        watcher.OnTransactionConfirmed += (_, _) => Interlocked.Increment(ref confirmedCount);

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(500);

        Assert.Equal(1, confirmedCount);
    }
```

- [ ] **Step 4: Run tests to verify failure**

Run: `dotnet test --filter "ConfirmationTracker"`
Expected: FAIL — no confirmation tracker implemented yet.

- [ ] **Step 5: Implement confirmation tracker**

Add pending transaction tracking and confirmation loop to `TronTransactionWatcher.cs`.

Add fields:

```csharp
    private readonly ConcurrentDictionary<string, PendingTx> _pendingTransactions = new();
    private readonly int _confirmationIntervalMs;
    private readonly TimeSpan _maxPendingAge;
    private Task? _confirmationTask;

    internal record PendingTx(string TxId, string ContractType, long BlockNumber, DateTimeOffset DiscoveredAt);
```

Add `using System.Collections.Concurrent;` at the top.

Update constructor to accept optional parameters:

```csharp
    public TronTransactionWatcher(ITronBlockStream stream, ITronProvider provider,
        int confirmationIntervalMs = 3000, TimeSpan? maxPendingAge = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _confirmationIntervalMs = confirmationIntervalMs;
        _maxPendingAge = maxPendingAge ?? TimeSpan.FromMinutes(5);
    }
```

Add pending tracking to `ProcessTransactionAsync` — replace the placeholder comment at the end of the method (added in Task 4) with:

```csharp
        // Track for confirmation (avoid duplicate for self-transfers)
        _pendingTransactions.TryAdd(tx.TxId,
            new PendingTx(tx.TxId, tx.ContractType, block.BlockNumber, DateTimeOffset.UtcNow));
```

This always adds because `ProcessTransactionAsync` is only called when at least one direction is watched (checked in `WatchLoopAsync`).

Update `StartAsync`:

```csharp
    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _watchTask = WatchLoopAsync(_cts.Token);
        _confirmationTask = ConfirmationLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }
```

Add the confirmation loop:

```csharp
    private async Task ConfirmationLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_confirmationIntervalMs, ct);
            }
            catch (OperationCanceledException) { return; }

            foreach (var kvp in _pendingTransactions)
            {
                if (ct.IsCancellationRequested) break;

                var pending = kvp.Value;

                // Check expiry
                if (DateTimeOffset.UtcNow - pending.DiscoveredAt > _maxPendingAge)
                {
                    if (_pendingTransactions.TryRemove(kvp.Key, out _))
                    {
                        OnTransactionFailed?.Invoke(this, new TransactionFailedEventArgs(
                            pending.TxId, pending.BlockNumber,
                            TransactionFailureReason.Expired,
                            "Transaction confirmation timed out"));
                    }
                    continue;
                }

                try
                {
                    var info = await _provider.GetTransactionInfoByIdAsync(pending.TxId, ct);
                    if (string.IsNullOrEmpty(info.TxId) || info.BlockNumber == 0)
                        continue; // Not yet on Solidity Node

                    if (!_pendingTransactions.TryRemove(kvp.Key, out _))
                        continue; // Already processed

                    // Check contract execution result for TRC20
                    if (pending.ContractType == "TriggerSmartContract" && IsContractFailed(info.ReceiptResult))
                    {
                        OnTransactionFailed?.Invoke(this, new TransactionFailedEventArgs(
                            pending.TxId, info.BlockNumber,
                            ParseFailureReason(info.ReceiptResult),
                            info.ReceiptResult));
                    }
                    else
                    {
                        var timestamp = info.BlockTimestamp > 0
                            ? DateTimeOffset.FromUnixTimeMilliseconds(info.BlockTimestamp)
                            : DateTimeOffset.UtcNow;
                        OnTransactionConfirmed?.Invoke(this, new TransactionConfirmedEventArgs(
                            pending.TxId, info.BlockNumber, timestamp));
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* provider error — retry next cycle */ }
            }
        }
    }

    private static bool IsContractFailed(string receiptResult)
    {
        if (string.IsNullOrEmpty(receiptResult))
            return false;
        return receiptResult != "SUCCESS" && receiptResult != "DEFAULT";
    }

    private static TransactionFailureReason ParseFailureReason(string? receiptResult) => receiptResult switch
    {
        not null when receiptResult.Contains("REVERT", StringComparison.OrdinalIgnoreCase)
            => TransactionFailureReason.ContractReverted,
        not null when receiptResult.Contains("OUT_OF_ENERGY", StringComparison.OrdinalIgnoreCase)
            => TransactionFailureReason.OutOfEnergy,
        not null when receiptResult.Contains("BANDWIDTH", StringComparison.OrdinalIgnoreCase)
            => TransactionFailureReason.OutOfBandwidth,
        _ => TransactionFailureReason.Other
    };
```

Update `StopAsync`:

```csharp
    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_watchTask != null)
        {
            try { await _watchTask; }
            catch (OperationCanceledException) { }
        }
        if (_confirmationTask != null)
        {
            try { await _confirmationTask; }
            catch (OperationCanceledException) { }
        }
        _pendingTransactions.Clear();
    }
```

- [ ] **Step 6: Run tests**

Run: `dotnet test --filter "Category!=Integration"`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/ChainKit.Tron/Watching/TronTransactionWatcher.cs tests/ChainKit.Tron.Tests/Watching/TronTransactionWatcherTests.cs
git commit -m "feat(watcher): add confirmation tracker with Solidity Node polling"
```

---

### Task 6: Add Failure and Expiry Detection (TDD)

**Files:**
- Test: `tests/ChainKit.Tron.Tests/Watching/TronTransactionWatcherTests.cs`
- Modify: `src/ChainKit.Tron/Watching/TronTransactionWatcher.cs` (if needed)

- [ ] **Step 1: Write test for TRC20 contract revert → Failed**

```csharp
    [Fact]
    public async Task ConfirmationTracker_Trc20Revert_FiresFailed()
    {
        var provider = Substitute.For<ITronProvider>();
        var contractAddr = "41" + new string('e', 40);
        var tx = MakeTrc20TxWithData(OtherAddr, contractAddr, WatchedAddr, 1_000_000, "revert_tx");
        var block = MakeBlock(1, tx);
        var stream = new MockBlockStream(block);

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        provider.GetTransactionInfoByIdAsync("revert_tx", Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto("revert_tx", 1, ts, "", 0, 0, 0,
                ReceiptResult: "REVERT"));

        await using var watcher = new TronTransactionWatcher(stream, provider,
            confirmationIntervalMs: 50);

        var tcs = new TaskCompletionSource<TransactionFailedEventArgs>();
        watcher.OnTransactionFailed += (_, e) => tcs.TrySetResult(e);

        // Ensure no Confirmed event fires
        var confirmedFired = false;
        watcher.OnTransactionConfirmed += (_, _) => confirmedFired = true;

        watcher.WatchAddress(contractAddr);
        await watcher.StartAsync();

        var failed = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("revert_tx", failed.TxId);
        Assert.Equal(TransactionFailureReason.ContractReverted, failed.Reason);
        Assert.False(confirmedFired);
    }

    [Fact]
    public async Task ConfirmationTracker_Trc20OutOfEnergy_FiresFailed()
    {
        var provider = Substitute.For<ITronProvider>();
        var contractAddr = "41" + new string('e', 40);
        var tx = MakeTrc20TxWithData(OtherAddr, contractAddr, WatchedAddr, 1_000_000, "oom_tx");
        var block = MakeBlock(1, tx);
        var stream = new MockBlockStream(block);

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        provider.GetTransactionInfoByIdAsync("oom_tx", Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto("oom_tx", 1, ts, "", 0, 0, 0,
                ReceiptResult: "OUT_OF_ENERGY"));

        await using var watcher = new TronTransactionWatcher(stream, provider,
            confirmationIntervalMs: 50);

        var tcs = new TaskCompletionSource<TransactionFailedEventArgs>();
        watcher.OnTransactionFailed += (_, e) => tcs.TrySetResult(e);

        watcher.WatchAddress(contractAddr);
        await watcher.StartAsync();

        var failed = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TransactionFailureReason.OutOfEnergy, failed.Reason);
    }
```

- [ ] **Step 2: Write test for TRX transfer always confirms (never fails)**

```csharp
    [Fact]
    public async Task ConfirmationTracker_TrxTransfer_AlwaysConfirms()
    {
        var provider = Substitute.For<ITronProvider>();
        var tx = MakeTrxTxWithAmount(OtherAddr, WatchedAddr, 10_000_000);
        var block = MakeBlock(1, tx);
        var stream = new MockBlockStream(block);

        // Even with a non-SUCCESS ReceiptResult, TRX transfers should confirm
        // (TRX native transfers don't have contract execution results)
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        provider.GetTransactionInfoByIdAsync("tx1", Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto("tx1", 1, ts, "", 0, 0, 0, ReceiptResult: "DEFAULT"));

        await using var watcher = new TronTransactionWatcher(stream, provider,
            confirmationIntervalMs: 50);

        var tcs = new TaskCompletionSource<TransactionConfirmedEventArgs>();
        watcher.OnTransactionConfirmed += (_, e) => tcs.TrySetResult(e);

        var failedFired = false;
        watcher.OnTransactionFailed += (_, _) => failedFired = true;

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();

        var confirmed = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("tx1", confirmed.TxId);
        Assert.False(failedFired);
    }
```

- [ ] **Step 3: Write test for expiry**

```csharp
    [Fact]
    public async Task ConfirmationTracker_Expired_FiresFailed()
    {
        var provider = Substitute.For<ITronProvider>();
        var tx = MakeTrxTxWithAmount(OtherAddr, WatchedAddr, 10_000_000);
        var block = MakeBlock(1, tx);
        var stream = new MockBlockStream(block);

        // Solidity Node never confirms — always returns empty
        provider.GetTransactionInfoByIdAsync("tx1", Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto("", 0, 0, "", 0, 0, 0));

        await using var watcher = new TronTransactionWatcher(stream, provider,
            confirmationIntervalMs: 50,
            maxPendingAge: TimeSpan.FromMilliseconds(200));

        var tcs = new TaskCompletionSource<TransactionFailedEventArgs>();
        watcher.OnTransactionFailed += (_, e) => tcs.TrySetResult(e);

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();

        var failed = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("tx1", failed.TxId);
        Assert.Equal(TransactionFailureReason.Expired, failed.Reason);
        Assert.Equal(1, failed.BlockNumber); // BlockNumber from discovery
    }
```

- [ ] **Step 4: Run tests**

Run: `dotnet test --filter "Category!=Integration"`
Expected: All tests pass — the implementation from Task 5 already handles these cases.

- [ ] **Step 5: Commit**

```bash
git add tests/ChainKit.Tron.Tests/Watching/TronTransactionWatcherTests.cs
git commit -m "test(watcher): add failure and expiry detection tests"
```

---

### Task 7: Lifecycle Cleanup and Final Verification (TDD)

**Files:**
- Test: `tests/ChainKit.Tron.Tests/Watching/TronTransactionWatcherTests.cs`
- Modify: `src/ChainKit.Tron/Watching/TronTransactionWatcher.cs` (if needed)

- [ ] **Step 1: Write test — Stop clears pending, no residual events**

```csharp
    [Fact]
    public async Task StopAsync_ClearsPending_NoResidualEvents()
    {
        var provider = Substitute.For<ITronProvider>();
        var tx = MakeTrxTxWithAmount(OtherAddr, WatchedAddr, 10_000_000);
        var block = MakeBlock(1, tx);
        var stream = new MockBlockStream(block);

        // Solidity Node never confirms
        provider.GetTransactionInfoByIdAsync("tx1", Arg.Any<CancellationToken>())
            .Returns(new TransactionInfoDto("", 0, 0, "", 0, 0, 0));

        var watcher = new TronTransactionWatcher(stream, provider,
            confirmationIntervalMs: 50);

        var confirmedCount = 0;
        var failedCount = 0;
        watcher.OnTransactionConfirmed += (_, _) => Interlocked.Increment(ref confirmedCount);
        watcher.OnTransactionFailed += (_, _) => Interlocked.Increment(ref failedCount);

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();
        await Task.Delay(100); // Let block process + at least one confirmation poll

        await watcher.StopAsync();
        await Task.Delay(200); // Wait to ensure no events fire after stop

        Assert.Equal(0, confirmedCount);
        Assert.Equal(0, failedCount);

        await watcher.DisposeAsync();
    }
```

- [ ] **Step 2: Write test — provider error doesn't crash confirmation loop**

```csharp
    [Fact]
    public async Task ConfirmationTracker_ProviderError_RetriesNextCycle()
    {
        var provider = Substitute.For<ITronProvider>();
        var tx = MakeTrxTxWithAmount(OtherAddr, WatchedAddr, 10_000_000);
        var block = MakeBlock(1, tx);
        var stream = new MockBlockStream(block);

        int callCount = 0;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        provider.GetTransactionInfoByIdAsync("tx1", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref callCount) <= 2)
                    throw new HttpRequestException("Temporary failure");
                return Task.FromResult(new TransactionInfoDto("tx1", 1, ts, "", 0, 0, 0,
                    ReceiptResult: "SUCCESS"));
            });

        await using var watcher = new TronTransactionWatcher(stream, provider,
            confirmationIntervalMs: 50);

        var tcs = new TaskCompletionSource<TransactionConfirmedEventArgs>();
        watcher.OnTransactionConfirmed += (_, e) => tcs.TrySetResult(e);

        watcher.WatchAddress(WatchedAddr);
        await watcher.StartAsync();

        var confirmed = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("tx1", confirmed.TxId);
        Assert.True(callCount >= 3);
    }
```

- [ ] **Step 3: Run all tests**

Run: `dotnet test --filter "Category!=Integration"`
Expected: All tests pass.

- [ ] **Step 4: Run build to verify no warnings**

Run: `dotnet build --warnaserror`
Expected: Clean build, no warnings.

- [ ] **Step 5: Commit**

```bash
git add tests/ChainKit.Tron.Tests/Watching/TronTransactionWatcherTests.cs
git commit -m "test(watcher): add lifecycle cleanup and error recovery tests"
```

- [ ] **Step 6: Update documentation references**

Update `docs/tron-transaction-lifecycle.md` — remove the "Watcher 已知限制" section items #1, #2, #3 since they are now resolved:
- ~~不檢查合約執行結果~~ → now checked via ReceiptResult
- ~~轉出交易資訊不完整~~ → now fires OnTrxSent/OnTrc20Sent with full info
- ~~OnTransactionConfirmed 名稱誤導~~ → now correctly fires from Solidity Node confirmation

Update `docs/tron-sdk-usage-guide.md` watcher section to document the new events and lifecycle.

- [ ] **Step 7: Commit docs**

```bash
git add docs/tron-transaction-lifecycle.md docs/tron-sdk-usage-guide.md
git commit -m "docs: update watcher lifecycle and usage guide for new events"
```
