# Tron 交易生命週期

## 概述

Tron 交易從建立到最終確認，經歷多個階段。TRC20 等智能合約交易比 TRX 原生轉帳多一層合約執行結果。

## 生命週期階段

### TRX 原生轉帳

```
簽名 → 廣播 → 打包進區塊 → Solidity Node 確認（不可逆）
                                    ↘ 或失敗
```

### TRC20 智能合約交易

```
簽名 → 廣播 → 打包進區塊 → Solidity Node 確認 → 合約執行結果
                                                    ├─ SUCCESS（轉帳成功）
                                                    ├─ REVERT（合約邏輯拒絕，如餘額不足）
                                                    └─ OUT_OF_ENERGY（執行中斷）
```

**重要：** TRC20 交易「上鏈成功 ≠ 合約執行成功」。一筆 Confirmed 的交易，合約內部可能 revert，錢沒有實際轉出。

## 確認機制：Full Node vs Solidity Node

| 節點類型 | 角色 | 何時能查到交易 |
|---------|------|--------------|
| Full Node | 收廣播、打包區塊 | 交易打包後立即可查 |
| Solidity Node | 僅包含已確認區塊 | 經過 19 個區塊確認（約 57 秒）後 |

SDK 透過同時查詢兩個節點來區分「已打包」和「已確認」。

## 高階 API 對應（`GetTransactionDetailAsync`）

| 階段 | TronResult | TransactionStatus |
|------|-----------|-------------------|
| 廣播未打包（極短暫，約 0-3 秒） | `Fail` — "Transaction not found" | — |
| 已打包，未確認 | `Ok` | `Unconfirmed` |
| 已確認 | `Ok` | `Confirmed` |
| 失敗 | `Ok` | `Failed` |

### 消費者判斷邏輯

```csharp
var result = await client.GetTransactionDetailAsync(txId);

if (!result.Success)
{
    // 查不到（還沒打包，或 provider 連線錯誤）
}
else switch (result.Data.Status)
{
    case TransactionStatus.Unconfirmed:
        // 已打包，等 Solidity Node 確認
        break;
    case TransactionStatus.Confirmed:
        // 已確認，不可逆
        // 若為 TRC20，檢查 result.Data.Failure 判斷合約是否成功
        break;
    case TransactionStatus.Failed:
        // 失敗，查看 result.Data.Failure 取得原因
        break;
}
```

### 失敗原因（`FailureReason`）

| 原因 | 說明 |
|------|------|
| `OutOfEnergy` | Energy 不足，合約未執行完 |
| `OutOfBandwidth` | Bandwidth 不足 |
| `InsufficientBalance` | 餘額不足 |
| `ContractReverted` | 合約主動 revert |
| `ContractOutOfTime` | 合約執行超時 |
| `InvalidSignature` | 簽名無效 |
| `Expired` | 交易過期 |
| `DuplicateTransaction` | 重複交易 |

## Tron 出塊特性

- 出塊時間：**3 秒**（DPoS 共識）
- 「廣播未打包」窗口極短（0-3 秒），實務上幾乎觀察不到
- 不像 Ethereum 有 gas 競價，交易不會長時間卡在 mempool

## Watcher 功能

### 雙向監聽

`TronTransactionWatcher` 同時監聽 incoming 和 outgoing 交易：
- `OnTrxReceived` / `OnTrc20Received` — 接收（watched address 是收款方）
- `OnTrxSent` / `OnTrc20Sent` — 轉出（watched address 是發送方）

### 交易生命週期

Watcher 追蹤三階段生命週期：

| 階段 | 事件 | 觸發時機 |
|------|------|----------|
| Unconfirmed | OnTrx/Trc20 Received/Sent | 交易出現在 block 中 |
| Confirmed | OnTransactionConfirmed | Solidity Node 確認（~57 秒） |
| Failed | OnTransactionFailed | 合約執行失敗或確認逾時 |

### TRC20 合約執行結果

確認追蹤器檢查 `ReceiptResult`：
- `SUCCESS` / `DEFAULT` → `OnTransactionConfirmed`
- `REVERT` / `OUT_OF_ENERGY` 等 → `OnTransactionFailed`
