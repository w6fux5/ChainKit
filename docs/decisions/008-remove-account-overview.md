# 008: 移除 GetAccountOverviewAsync

## 背景

`TronClient` 提供三個帳戶查詢 API：

| API | 功能 |
|-----|------|
| `GetBalanceAsync` | TRX 餘額 + TRC20 餘額 |
| `GetAccountOverviewAsync` | TRX 餘額 + Bandwidth/Energy + 最近 10 筆交易 |
| `GetResourceInfoAsync` | Bandwidth/Energy + 質押量 + 委託資訊 |

`GetAccountOverviewAsync` 的問題：

1. **職責不清**：帳戶摘要不應該包含交易查詢，讓 API 又慢又重
2. **額外 API call**：`GetAccountTransactionsAsync`（TronGrid V1 API）增加延遲和失敗風險
3. **狀態判斷不一致**：內部用 `ContractResult.Contains("SUCCESS")` 判斷交易狀態，與 `DetermineStatus` 使用 `receipt.result` 的邏輯不同，是潛在 bug
4. **功能重疊**：TRX 餘額 = `GetBalanceAsync`，Bandwidth/Energy = `GetResourceInfoAsync`

## 決策

移除 `GetAccountOverviewAsync` 和 `AccountOverview` model。Consumer 按需組合：

- 查餘額 → `GetBalanceAsync`
- 查資源 → `GetResourceInfoAsync`
- 查交易 → `GetTransactionDetailAsync` 或 `ITronProvider.GetAccountTransactionsAsync`

## 原因

- 三個獨立 API 職責清楚，consumer 只呼叫需要的
- 避免交易狀態判斷邏輯分散在多處
- 減少不必要的 API call

## 影響範圍

- 移除 `TronClient.GetAccountOverviewAsync`
- 移除 `AccountOverview` record
- 移除 Sandbox `/api/account/{address}/overview` endpoint
- 移除相關測試（4 個單元測試 + 1 個 E2E 測試）
- 更新使用指南和 Sandbox README
