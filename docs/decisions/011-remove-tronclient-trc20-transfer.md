# ADR 011: 移除 TronClient.TransferTrc20Async，TRC20 操作統一走 Trc20Contract

## 背景

SDK 有兩個 TRC20 轉帳 API：

1. `TronClient.TransferTrc20Async(account, contract, to, amount, decimals)` — 呼叫端必須自己傳 `decimals`
2. `Trc20Contract.TransferAsync(to, amount)` — 自動從合約查詢 `decimals`

兩者底層做的事完全一樣（ABI 編碼 → TriggerSmartContract → 簽名 → 廣播），
但「高階」API 反而要求更多輸入（`decimals`），違反封裝原則。

同時 `TronClient` 只包了 TRC20 的 transfer，沒有 approve/mint/burn 等操作，
consumer 做其他操作還是要用 `Trc20Contract`，功能切分不完整。

## 決策

- 移除 `TronClient.TransferTrc20Async`
- TRC20 所有操作統一透過 `Trc20Contract`（transfer、approve、mint、burn、balanceOf 等）
- `TronClient.GetTrc20Contract()` 工廠方法保留，作為取得 `Trc20Contract` 的入口

### 新增高階 API

- `Trc20Contract.GetTokenInfoAsync()` — 五個查詢並行（name、symbol、decimals、totalSupply、originAddress）
  回傳 `Trc20TokenInfo` record
- `ITronProvider.GetContractAsync()` — 呼叫 `/wallet/getcontract` 取得合約部署者地址（`origin_address`）
- HTTP 和 gRPC provider 都有實作

### Sandbox 對應調整

- 移除重複的 `/api/trc20/transfer`（高階），保留 `Trc20Contract` 版並改路徑為 `/api/trc20/transfer`
- 新增 `GET /api/trc20/{contractAddress}/info`（高階，一次回傳全部代幣資訊）

## 放棄的方案

- 讓 `TronClient.TransferTrc20Async` 的 `decimals` 變 optional：
  減少重複但職責切分仍然不清楚，TronClient 只有 transfer 沒有其他 TRC20 操作
- 在 TronClient 加入所有 TRC20 操作：
  完全重複 `Trc20Contract`，維護成本加倍

## 影響範圍

- 移除 `TronClient.TransferTrc20Async` 和 3 個對應單元測試
- E2E 測試改用 `Trc20Contract.TransferAsync`
- 新增 `Trc20TokenInfo`、`SmartContractInfo` DTO
- 新增 `ITronProvider.GetContractAsync` + HTTP/gRPC 實作
