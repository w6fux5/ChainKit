# ADR 018: 節點健康檢查採用 event-based polling + raw metrics

## 狀態

已實作（2026-04-17）

## 背景

應用端需要知道底層 Tron / EVM 節點是否健康。之前沒有第一方 API，caller 必須自己寫 ad-hoc probe（呼叫 GetBlockNumber、catch exception、計時），重複且容易出錯。

## 決策

新增 `TronNodeHealthWatcher` / `EvmNodeHealthWatcher`，採用以下設計：

1. **Event-based polling**：每隔 `intervalMs` 毫秒 probe 節點，觸發 `OnHealthChecked` 事件
2. **每次 poll 都觸發**：不做 state-change filtering（每次 poll 的結果都推給 caller）
3. **回報 raw metrics**：Reachable / Latency / BlockNumber / BlockAge / ChainIdMatch（EVM only）
4. **Caller 自定義 threshold**：SDK 不判斷「什麼是健康」——latency 多少算慢、block age 多久算異常，由 app 決定

## 原因

### 為什麼是 event-based（不是 method call）

使用者明確要求「像 ZMQ 那種監聽模式」。與既有 `TronTransactionWatcher` / `EvmTransactionWatcher` 對齊，降低學習曲線。

### 為什麼每次 poll 都觸發（不是只在 state change 時）

- 「健康 threshold」是業務決策（不同鏈、不同專案、不同環境的容忍度不同），SDK 不該 bake-in
- 每次 poll event 讓 caller 有完整時間序列資料，可自行實作 state tracking、smoothing、alerting
- 若未來需要 state-change event，可以 additive 疊上去（不是 breaking change）

### 為什麼 Tron 不做 ChainIdMatch

Tron 協定沒有原生 chainId 概念（交易不包含 chainId 欄位）。使用者設定 endpoint 時就已經選定網路，無法再驗證。

### 為什麼 Tron 只 probe Full Node（不含 Solidity Node）

MVP 決策。Solidity Node 是確認用途，對「節點是否活著」的判斷非必要。未來可 additive 加入。

## EVM ChainId 快取策略

首次成功 poll 時呼叫 `eth_chainId`，快取結果。後續 poll 不再呼叫（節點的 chainId 不會在 runtime 改變）。`ChainIdMatch = cachedChainId == network.ChainId`，未成功取得過則為 `null`。

此策略需要 `IEvmProvider.GetChainIdAsync()` 方法（本次同步 additive 加入）。

## 放棄的方案

- **只在 state change 時觸發 event**：SDK 需內建 threshold 規則，主觀且不彈性
- **Method call（pull model）**：caller 自己 loop + Timer，重複實作
- **Multi-provider health-based failover**：超出 SDK 健康檢查範疇，屬於上層 facade 責任

## 後果

- Watcher pattern 與交易監聽對稱（IAsyncDisposable、StartAsync/StopAsync、EventHandler）
- 14 個事件中的 2 個屬於健康檢查
- caller 需要自己寫 threshold 判斷邏輯（trade-off：彈性 vs 便利）
- 詳細設計見 `docs/superpowers/specs/2026-04-16-node-health-watcher-design.md`
