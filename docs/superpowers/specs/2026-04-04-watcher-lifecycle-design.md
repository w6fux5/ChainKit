# Watcher 生命周期增強設計

## 概述

增強 `TronTransactionWatcher`，支援雙向監聽（incoming + outgoing）與三階段交易生命周期（Unconfirmed → Confirmed / Failed）。

## 現狀問題

1. **只監聽 incoming** — `OnTrxReceived` / `OnTrc20Received` 僅在接收方是 watched address 時觸發，轉出不觸發
2. **沒有生命周期追蹤** — 交易出現在 block 就觸發事件，不區分 Unconfirmed / Confirmed / Failed
3. **不檢查 TRC20 合約執行結果** — 上鏈不代表合約成功，consumer 需自行二次確認
4. **`OnTransactionConfirmed` 名稱誤導** — 實際是「打包進 block」，不是 Solidity Node 確認

## 設計

### 事件體系

6 個事件，分兩層：

```
發現交易（Unconfirmed，觸發一次）     最終狀態（觸發一次）
──────────────────────────────      ─────────────────
OnTrxReceived                       OnTransactionConfirmed
OnTrxSent                           OnTransactionFailed
OnTrc20Received
OnTrc20Sent
```

### EventArgs

```csharp
// 收發事件 — Unconfirmed 時觸發一次
record TrxReceivedEventArgs(string TxId, string FromAddress, string ToAddress,
    decimal Amount, long BlockNumber, DateTimeOffset Timestamp);

record TrxSentEventArgs(string TxId, string FromAddress, string ToAddress,
    decimal Amount, long BlockNumber, DateTimeOffset Timestamp);

record Trc20ReceivedEventArgs(string TxId, string FromAddress, string ToAddress,
    string ContractAddress, string Symbol, decimal RawAmount, decimal? Amount,
    int Decimals, long BlockNumber, DateTimeOffset Timestamp);

record Trc20SentEventArgs(string TxId, string FromAddress, string ToAddress,
    string ContractAddress, string Symbol, decimal RawAmount, decimal? Amount,
    int Decimals, long BlockNumber, DateTimeOffset Timestamp);

// 最終狀態事件
record TransactionConfirmedEventArgs(string TxId, long BlockNumber, DateTimeOffset Timestamp);

record TransactionFailedEventArgs(string TxId, long BlockNumber,
    TransactionFailureReason Reason, string? Message);
```

### TransactionFailureReason

```csharp
public enum TransactionFailureReason
{
    ContractReverted,    // 合約主動 revert（餘額不足等）
    OutOfEnergy,         // Energy 不夠，執行中斷
    OutOfBandwidth,      // Bandwidth 不足
    Expired,             // 超過 maxAge，Solidity Node 始終查不到
    Other                // 其他未預期的失敗
}
```

### Watcher 公開 API

```csharp
public class TronTransactionWatcher : IAsyncDisposable
{
    // provider 從 optional 變 required
    public TronTransactionWatcher(ITronBlockStream stream, ITronProvider provider);

    // 地址管理（不變）
    public void WatchAddress(string address);
    public void WatchAddresses(IEnumerable<string> addresses);
    public void UnwatchAddress(string address);

    // 發現交易事件（Unconfirmed 觸發一次）
    public event EventHandler<TrxReceivedEventArgs>? OnTrxReceived;
    public event EventHandler<TrxSentEventArgs>? OnTrxSent;
    public event EventHandler<Trc20ReceivedEventArgs>? OnTrc20Received;
    public event EventHandler<Trc20SentEventArgs>? OnTrc20Sent;

    // 最終狀態事件
    public event EventHandler<TransactionConfirmedEventArgs>? OnTransactionConfirmed;
    public event EventHandler<TransactionFailedEventArgs>? OnTransactionFailed;

    // 生命周期（不變）
    public Task StartAsync(CancellationToken ct = default);
    public Task StopAsync();
    public ValueTask DisposeAsync();
}
```

### 架構分層

```
發現交易（Unconfirmed）
├─ PollingBlockStream → ITronProvider（HTTP 或 gRPC）
└─ ZmqBlockStream     → ZMQ（需自建節點）

確認結果（Confirmed / Failed）
└─ Confirmation Tracker（watcher 內建）→ ITronProvider（HTTP 或 gRPC）
```

- 發現交易透過 `ITronBlockStream`，支援 HTTP/gRPC polling 或 ZMQ 推送
- 確認追蹤透過 `ITronProvider` 查詢 Solidity Node，不管發現方式是哪種
- 兩個階段可以用不同的 transport，自由組合

### Block 處理邏輯

```
每筆交易：
  from = tx.FromAddress
  to   = tx.ToAddress（TRC20 解析 ABI 取得 recipient）

  fromWatched = _watchedAddresses.Contains(from)
  toWatched   = _watchedAddresses.Contains(to)

  if toWatched   → fire OnTrxReceived 或 OnTrc20Received → 加入 pending
  if fromWatched → fire OnTrxSent 或 OnTrc20Sent → 加入 pending（避免重複）
```

**自己轉給自己（from == to）：** 同時觸發 Received + Sent，pending 只追蹤一次，Confirmed/Failed 只 fire 一次。

### Confirmation Tracker

Watcher 內建，背景 loop 輪詢 Solidity Node：

```
背景 loop（每 3 秒一輪）：
  遍歷 _pendingTransactions：
    呼叫 provider.GetTransactionInfoByIdAsync(txId)

    查不到 → 保留 pending，下輪再查

    查到（TRX 原生轉帳）→ fire Confirmed → 移出 pending

    查到（TRC20）：
      contractResult == SUCCESS → fire Confirmed → 移出 pending
      contractResult == REVERT / OUT_OF_ENERGY 等 → fire Failed(reason) → 移出 pending

    超過 maxAge（預設 5 分鐘）→ fire Failed(Expired) → 移出 pending
```

**PendingTx 結構：**
```csharp
record PendingTx(string TxId, string ContractType, long BlockNumber, DateTimeOffset DiscoveredAt);
```

- `ContractType`：區分 TRX 原生轉帳 vs TRC20（TriggerSmartContract），用於判定合約執行結果
- `BlockNumber`：發現時的 block number，Expired 時作為 `TransactionFailedEventArgs.BlockNumber` 使用

**狀態管理：**
- `ConcurrentDictionary<string, PendingTx>` 儲存 pending 交易
- 自己轉給自己時，同一筆 tx 只追蹤一次
- Watcher stop/dispose 時清理 pending set，不 fire 殘留事件
- 輪詢間隔預設 3 秒，與 block stream 間隔獨立

### Breaking Changes

1. `ITronProvider` 建構子參數從 optional 變 required
2. `OnTransactionConfirmed` 語意改變：從「打包進 block」變成「Solidity Node 確認」
3. `TransactionConfirmedEventArgs` 移除 `Success` 欄位（成功/失敗用不同事件區分）

### 測試策略

- 單元測試：mock `ITronBlockStream` + mock `ITronProvider`，驗證事件觸發邏輯
  - incoming TRX/TRC20 觸發 Received
  - outgoing TRX/TRC20 觸發 Sent
  - 自己轉自己同時觸發 Received + Sent
  - Confirmation tracker：mock Solidity Node 回應，驗證 Confirmed / Failed 觸發
  - TRC20 合約 revert 觸發 Failed
  - 超時觸發 Failed(Expired)
  - Stop/Dispose 不 fire 殘留事件
- E2E 測試：Nile 測試網，實際 broadcast + 等待確認
