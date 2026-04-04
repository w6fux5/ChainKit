# 007: Watcher 事件地址格式統一為 Base58

## 背景

Watcher 事件（TrxReceived/Sent、Trc20Received/Sent）回傳的 `FromAddress`、`ToAddress`、`ContractAddress` 是 hex 格式（`41` 開頭），但 SDK 其他高階 API（`TronClient`）回傳的地址都是 Base58 格式（`T` 開頭）。Consumer 收到事件後需要自行轉換才能使用。

## 決策

在 Watcher 觸發事件時，將 hex 地址轉換為 Base58 格式：

- 轉換位置：`TronTransactionWatcher` 事件觸發前
- 不改 provider 層（低階 API 保持 hex 合理）
- 不改 `TronBlockTransaction` 結構（內部資料結構）

```csharp
private static string FormatAddress(string address)
{
    if (string.IsNullOrEmpty(address)) return address;
    try { return TronAddress.IsValid(address) && address.StartsWith("41") 
        ? TronAddress.ToBase58(address) : address; }
    catch { return address; }
}
```

## 原因

- 高階 API 的一致性：consumer 不應該需要關心地址格式轉換
- Watcher 是高階功能，應該跟 `TronClient` 的行為一致
- `WatchAddress` 接受 hex 和 Base58 兩種格式，事件回傳統一為 Base58

## 同時修正：Sandbox SSE EventBus

原本 `WatcherEventBus` 使用單一 `Channel`，只支援一個 reader。SSE 連線斷開重連後新連線無法收到事件。改為多 subscriber 廣播模式，每個 SSE 連線有獨立的 Channel。
