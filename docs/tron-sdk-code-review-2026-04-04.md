# Tron SDK Code Review Report

**Date:** 2026-04-04
**Scope:** Full review of `src/ChainKit.Tron/` + `src/ChainKit.Core/` (27 source files)
**Build:** Clean (0 compiler warnings on SDK projects, 0 vulnerable packages)
**Framework:** .NET 10, Nullable enabled, ImplicitUsings enabled

---

## Critical

### C-1: `JsonDocument` not disposed — memory leak under load

**12 occurrences** in `TronHttpProvider.cs`

```csharp
// Every API call does this:
var root = JsonDocument.Parse(json).RootElement;  // JsonDocument never disposed
```

**Lines:** 72, 136, 191, 239, 264, 297, 310, 378, 414, 477, 566, 616

**Why it matters:** `JsonDocument` rents arrays from `ArrayPool<byte>`. Without `Dispose()`, rented buffers are never returned. Under sustained load (e.g., watcher polling every 3s), this causes monotonically growing memory pressure and `ArrayPool` fragmentation.

**Fix:** Change every occurrence to:

```csharp
using var doc = JsonDocument.Parse(json);
var root = doc.RootElement;
```

Note: `RootElement` becomes invalid after `doc` is disposed, so ensure all value extraction happens within the `using` scope. Since each method already extracts all values before returning, this is a safe change.

---

### C-2: Floating-point precision loss in token amount calculations

**`TronConverter.ToRawAmount`** (`TronConverter.cs:43`):

```csharp
var multiplier = (decimal)Math.Pow(10, decimals);  // double → decimal = precision loss
return new BigInteger(amount * multiplier);
```

**Same pattern in:**

- `Trc20Contract.ToRawAmount` (`Trc20Contract.cs:289`)
- `TronClient.TransferTrc20Async` (`TronClient.cs:95`)
- `TronClient.GetBalanceAsync` (`TronClient.cs:296`) — division path
- `TronTransactionWatcher.ProcessTransactionAsync` (`TronTransactionWatcher.cs:169`)

**Why it matters:** `Math.Pow(10, 18)` returns `double` `1000000000000000000.0`, but `double` only has ~15-17 significant digits. When cast to `decimal` and multiplied with a fractional token amount, the result can be off by 1 or more in the smallest unit. For an SDK handling real financial transactions, this is unacceptable.

**Irony:** `TronConverter.ToTokenAmount` (the reverse operation) already uses the correct approach with `BigInteger.Pow(10, decimals)`.

**Fix:**

```csharp
public static BigInteger ToRawAmount(decimal amount, int decimals)
{
    if (decimals <= 0) return new BigInteger(amount);
    // Compute multiplier as decimal without double intermediary
    var multiplier = 1m;
    for (int i = 0; i < decimals; i++) multiplier *= 10;
    return new BigInteger(amount * multiplier);
}
```

---

## Warning

### W-1: Private key material never zeroed

`TronAccount.cs:13` — `public byte[] PrivateKey { get; }` stores the raw private key as a mutable `byte[]` with no mechanism to clear it.

- The class doesn't implement `IDisposable`
- Key material remains in memory until GC collects it
- Even after collection, the bytes may remain in memory pages

For an SDK handling cryptocurrency private keys, consider:

- Making `PrivateKey` internal (callers rarely need raw access)
- Adding `IDisposable` with `CryptographicOperations.ZeroMemory(PrivateKey)` in `Dispose()`

### W-2: Wrong error code for amount validation

`TronClient.cs` uses `TronErrorCode.InvalidAddress` for non-address errors:

| Location | Message | Should be |
|---|---|---|
| `TronClient.cs:38` | "Amount must be positive" | `InvalidAmount` or similar |
| `TronClient.cs:44` | "Amount too large" | `InvalidAmount` |
| `TronClient.cs:87-88` | "Invalid decimals value" | `ContractValidationFailed` |

Same pattern in `StakeTrxAsync`, `UnstakeTrxAsync`, `DelegateResourceAsync`, `UndelegateResourceAsync`.

**Fix:** Add an `InvalidAmount` variant to `TronErrorCode` and use it for amount-related validation.

### W-3: Silent exception swallowing without logging

Multiple `catch { }` blocks throughout the codebase discard exceptions without any logging:

| File:Line | Context |
|---|---|
| `TronClient.cs:154` | Solidity node failure treated as "unconfirmed" |
| `TronClient.cs:302` | TRC20 balance query failure → reports 0 |
| `TronClient.cs:651,681,689` | Delegation queries failure |
| `TokenInfoCache.cs:90,99` | Token metadata resolution failure |
| `TronTransactionWatcher.cs:264` | Provider error during confirmation check |
| `PollingBlockStream.cs:51` | Provider error during block polling |

While "best-effort" semantics are appropriate here (as documented), adding a diagnostic log at `Debug`/`Trace` level would make production troubleshooting significantly easier without changing the behavior.

### W-4: `TronHttpProvider` creates `HttpClient` directly

`TronHttpProvider.cs:47`: `_httpClient = new HttpClient()` creates a new `HttpClient` per provider instance. While the class properly disposes it (`_ownsHttpClient`), applications creating/disposing `TronHttpProvider` frequently will hit socket exhaustion from `TIME_WAIT` sockets.

The internal constructor accepting `HttpClient` exists for testing, but the public API doesn't support `IHttpClientFactory` or externally-managed `HttpClient` injection for production use. Consider adding a public constructor overload accepting `HttpClient`.

---

## Info

### I-1: Code duplication in `AbiEncoder`

6 encoding methods (`EncodeTransfer`, `EncodeApprove`, `EncodeMint`, `EncodeBurn`, `EncodeBurnFrom`, `EncodeAllowance`) share identical structure: selector + params → concat. A generic `EncodeCall(string signature, params byte[][] args)` would eliminate ~80 lines.

### I-2: Code duplication in `TronClient` transaction methods

`TransferTrxAsync`, `StakeTrxAsync`, `UnstakeTrxAsync`, `DelegateResourceAsync`, `UndelegateResourceAsync` all follow: validate → get block → build tx → sign → broadcast → map result. The error handling and broadcast logic is copy-pasted across all five.

### I-3: Missing `ConfigureAwait(false)` in library code

As a library, all `await` calls should use `ConfigureAwait(false)` to avoid capturing `SynchronizationContext`. In .NET 10 this is effectively a no-op for ASP.NET Core consumers, but library callers using WPF/WinForms would deadlock if calling `.Result` on the returned task. Low practical risk given "internal use" context.

### I-4: No `TreatWarningsAsErrors` or `Directory.Build.props`

`ChainKit.Tron.csproj` doesn't set `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. No shared `Directory.Build.props` exists to centralize settings. As the SDK grows to more chains, settings drift between projects becomes likely.

### I-5: Classes not sealed

`TronHttpProvider`, `TronGrpcProvider`, `PollingBlockStream`, `ZmqBlockStream`, `Trc20Contract`, `TronTransactionWatcher` are not sealed. Sealing enables devirtualization JIT optimizations and signals design intent.

---

## Top 5 Prioritized Recommendations

1. **Fix `JsonDocument` disposal** (C-1) — Easy mechanical fix, prevents real memory leak under production load. Highest impact-to-effort ratio.

2. **Fix `Math.Pow` precision loss** (C-2) — Financial correctness bug. Use `decimal` multiplication loop or `BigInteger.Pow` consistently. Align `ToRawAmount` with the already-correct `ToTokenAmount`.

3. **Add `InvalidAmount` error code** (W-2) — Quick fix, improves API correctness. Callers matching on error codes will get wrong matches today.

4. **Add public `HttpClient` injection** (W-4) — Add a `TronHttpProvider(HttpClient httpClient, string baseUrl, string? solidityUrl)` public constructor. Production apps need control over `HttpClient` lifetime.

5. **Centralize project settings** (I-4) — Create `Directory.Build.props` with `TreatWarningsAsErrors`, `Nullable`, `ImplicitUsings`, `LangVersion` to prevent drift as more chains are added.
