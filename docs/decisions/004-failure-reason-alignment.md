# 004: TransactionFailureReason 對齊 Tron Protobuf 定義

## 背景

Watcher 的 `TransactionFailureReason` enum 用於 `OnTransactionFailed` 事件，告訴消費者交易失敗的原因。初版設計包含 `OutOfBandwidth`，但 code review 發現這個值不會出現在 Solidity Node 的 `ReceiptResult` 中。

Watcher 的確認追蹤器透過 `GetTransactionInfoByIdAsync` 取得 `ReceiptResult`，這個值來自 Tron protobuf 的 `Transaction.Result.contractResult` enum：

```protobuf
enum contractResult {
    DEFAULT = 0;
    SUCCESS = 1;
    REVERT = 2;
    OUT_OF_ENERGY = 10;
    OUT_OF_TIME = 11;
    TRANSFER_FAILED = 14;
    // ... 其他 EVM 內部錯誤
}
```

## 決策

- 移除 `OutOfBandwidth`（protobuf enum 不存在此值，不會出現在 ReceiptResult）
- 新增 `OutOfTime`（合約執行超時，對應 `OUT_OF_TIME = 11`）
- 新增 `TransferFailed`（合約層轉帳失敗，對應 `TRANSFER_FAILED = 14`）
- `ParseFailureReason` 改用精確字串比對（不再用 `Contains`）

## 修改後的 enum

```csharp
public enum TransactionFailureReason
{
    ContractReverted,   // REVERT
    OutOfEnergy,        // OUT_OF_ENERGY
    OutOfTime,          // OUT_OF_TIME
    TransferFailed,     // TRANSFER_FAILED
    Expired,            // Watcher 自己的超時
    Other               // 其他 EVM 內部錯誤（BAD_JUMP_DESTINATION 等）
}
```

## 原因

1. **`OutOfBandwidth` 不會出現在 ReceiptResult** — Bandwidth 不足的交易在廣播階段就被拒絕，不會進入區塊，Watcher 看不到
2. **`OUT_OF_TIME` 是合理的失敗原因** — 合約執行超時，雖然罕見但有意義
3. **`TRANSFER_FAILED` 是合理的失敗原因** — 合約內部轉帳失敗
4. **精確比對更安全** — ReceiptResult 的值來自我們自己控制的 parse 邏輯，不需要模糊匹配

## 放棄的方案

**直接複用 `TronClient` 的 `FailureReason` enum：** `FailureReason` 包含 `InvalidSignature`、`DuplicateTransaction` 等不會出現在 Watcher 場景的值。Watcher 有自己的 `Expired` 語意。分開的 enum 更清晰。
