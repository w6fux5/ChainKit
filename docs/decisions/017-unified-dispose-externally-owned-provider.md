# ADR 017: 統一 Client.Dispose 行為 — Provider 由呼叫端負責清理

## 狀態

已實作（2026-04-17）

## 背景

`TronClient.Dispose()` 會嘗試 dispose 注入的 `ITronProvider`（如果實作 `IDisposable`），但 `EvmClient.Dispose()` 是空操作，註解寫「Provider is externally owned」。

這造成行為不一致：共享同一個 Provider 給多個 TronClient 時，任一 Client dispose 會殺掉 Provider，導致其他 Client 壞掉。

## 決策

**統一為 EvmClient 的模式**：`TronClient.Dispose()` 改為空操作，不再 dispose Provider。Provider 由呼叫端（通常是 DI container 或上層 using scope）負責清理。

## 原因

- 符合 .NET DI 慣例：注入的 dependency 通常由 container 管理生命週期，不由消費者 dispose
- Provider 可能是 singleton（如 `TronHttpProvider` 共用 HttpClient pool），不應被單一 Client 清理
- 與 EvmClient 統一，減少跨鏈認知差異

## 放棄的方案

- **維持 TronClient dispose Provider**：簡化單次使用場景（不用另外 dispose Provider），但共享場景不安全
- **參數化 ownership（`bool ownsProvider` 建構子參數）**：最彈性但增加 API 複雜度；目前使用場景不需要

## 後果

- 呼叫端必須自行管理 Provider 生命週期：
  ```csharp
  using var provider = new TronHttpProvider(...);
  using var client = new TronClient(provider);
  // client.Dispose() 不會動到 provider
  // provider 在自己的 using scope 結束時才 dispose
  ```
- 原本依賴 `TronClient.Dispose()` 清理 Provider 的 code 需要遷移
