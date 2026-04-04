# 006: 交易確認狀態判斷修正

## 背景

2026-04-04 手動測試發現 `GetTransactionDetailAsync` 對剛廣播的交易回傳 `status: 1`（Confirmed），但 Nile Scan 顯示交易仍為 Unconfirmed。266 個單元測試全部通過，未偵測到此 bug。

## 問題根因

### Bug 1：ParseTransactionInfo 空物件處理

`TronHttpProvider.ParseTransactionInfo` 在解析 Solidity Node 回傳的空物件 `{}` 時，使用呼叫端傳入的 `txId` 作為 fallback：

```csharp
// 修正前
var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? txId : txId;
```

Solidity Node 回傳 `{}`（交易未確認）→ JSON 沒有 `id` 欄位 → `TxId` 被填入呼叫端的 txId → `DetermineStatus` 看到非空 TxId → 誤判為 Confirmed。

### Bug 2：Smart Contract 失敗判斷不符合 Tron 文件

```csharp
// 修正前：檢查 ContractResult 是否含 "FAIL"
solidityInfo.ContractResult.Contains("FAIL")

// Tron 文件要求：檢查 receipt.result 是否為 "SUCCESS"
```

### Bug 3：同樣的 fallback 問題存在於 4 處

- `TronHttpProvider.ParseTransactionInfo`（Solidity Node HTTP）
- `TronHttpProvider.ParseTransactionInfoFromTx`（Full Node HTTP）
- `TronGrpcProvider.ParseTransactionInfo`（Solidity Node gRPC）
- `TronGrpcProvider.GetTransactionByIdAsync`（Full Node gRPC）

其中 gRPC Full Node 的 Transaction protobuf 沒有 txID 欄位（txID 是 raw_data 的 SHA256 hash），因此在交易存在時保留使用呼叫端 txId。

## 決策

### 修正 ParseTransactionInfo

空物件 `{}` 回傳空字串 TxId，不使用呼叫端的 txId 填充：

```csharp
// 修正後
var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
```

### 修正 DetermineStatus

使用 `ReceiptResult` 判斷 Smart Contract 交易狀態，符合 Tron 官方文件：

```csharp
// System Contract：solidityInfo 有資料 = Confirmed
// Smart Contract：receipt.result == SUCCESS = Confirmed，其他 = Failed
if (!string.IsNullOrEmpty(solidityInfo.ReceiptResult) &&
    !solidityInfo.ReceiptResult.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase))
    return TransactionStatus.Failed;
```

### 補充測試

新增 6 個測試覆蓋真實場景：
- Solidity Node 回傳空物件 `{}` → TxId 為空
- 空 TxId → Unconfirmed
- Smart Contract REVERT / OUT_OF_ENERGY → Failed
- Smart Contract SUCCESS → Confirmed
- System Contract 無 receipt → Confirmed

## Tron 交易確認機制（官方文件）

| 交易類型 | 確認方式 |
|----------|----------|
| System Contract（TRX 轉帳） | `/walletsolidity/gettransactioninfobyid` 查得到 = Confirmed |
| Smart Contract（TRC20 等） | 查得到 + `receipt.result == SUCCESS` = Confirmed |

- Full Node（port 8090）：`/wallet/` 端點，交易上鏈即回傳，不代表已確認
- Solidity Node（port 8091）：`/walletsolidity/` 端點，只回傳已 solidified 的資料
- Solidification 延遲約 19 個區塊（~57 秒）
- Full Node **不處理** `/walletsolidity/` 路徑（回傳 405 錯誤）

## 測試為什麼沒抓到

單元測試 mock `ITronProvider` 直接回傳建好的 `TransactionInfoDto`，繞過了 JSON 解析。Unconfirmed 測試假設「Solidity Node throw exception = 未確認」，但真實情況是「Solidity Node 回傳空物件 `{}` = 未確認」。測試驗證的是錯的假設，而非真實的節點行為。

## 放棄的方案

- **比對區塊號**：用交易的 block number 和最新 solidified block number 比較。每次多一個 API call，且 Tron 官方文件明確指出 `/walletsolidity/` 端點就是用來判斷確認狀態的標準做法，不需要額外比對。
