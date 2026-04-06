# ADR 010: SDK Logging 機制與序列化修正

## 背景

Code review（2026-04-04）遺留兩項技術債：

1. **W-3**：18 處 `catch {}` 靜默吞異常，生產環境無法觀察錯誤
2. **SnakeCaseLower 序列化風險**：`TronHttpProvider` 全域用 `JsonNamingPolicy.SnakeCaseLower`，
   導致 camelCase 的 API key（如 `fromAddress`）被轉成 `from_address`，
   Tron 節點回傳空結果（見 ADR 009 的 `PostRawAsync` workaround）

## 決策

### Logging

- 加入 `Microsoft.Extensions.Logging.Abstractions` 套件（10.0.5）
- 6 個 class 加入 optional `ILogger<T>?` 參數（預設 `NullLogger`，不是 breaking change）：
  TronClient、TronHttpProvider、TronTransactionWatcher、PollingBlockStream、ZmqBlockStream、TokenInfoCache
- 11 處 instance method catch block 加入 structured log（Debug/Warning）
- 7 處 static parsing method catch block 維持原樣（純資料轉換，失敗是正常 code path）

**Log Level 分類**：
- `Trace`：資料解析 fallback（hex/protobuf）
- `Debug`：單筆查詢失敗但有 fallback、provider 重試
- `Warning`：整批操作失敗（如 delegation 全部查詢失敗）

### 序列化

- `JsonNamingPolicy.SnakeCaseLower` → `JsonNamingPolicy.CamelCase`
- 移除 delegation API 的 `PostRawAsync` 手動 JSON workaround，改回正規 `PostAsync`

**為什麼安全**：所有現有 anonymous object 的 property name 都是小寫開頭（`address`、`visible`、`owner_address`），
`CamelCase` policy 對這些不做任何轉換。唯一會變的是多詞 camelCase 屬性（如 `fromAddress`），
而 Tron API 本身就期望 camelCase。

## 放棄的方案

### Logging
- `DiagnosticSource`：過重，適合框架級 tracing，不適合 SDK 的 catch-and-continue
- 自訂 callback/event：非標準，呼叫端要學新 API
- `EventSource`（ETW）：Windows-centric，消費端複雜

### 序列化
- 保留 SnakeCaseLower + 逐個 `PostRawAsync`：手動組 JSON 易出錯、無型別檢查
- 移除 NamingPolicy + 全部 `[JsonPropertyName]`：改動量大
- 雙 JsonOptions（snake + camel）：增加認知負擔

## 影響範圍

- `src/ChainKit.Tron/` 所有主要 class
- `sandbox/ChainKit.Sandbox/Program.cs`（DI 注入 logger）
- 所有測試通過，無 breaking change
