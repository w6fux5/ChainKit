# 005: Code Review 修復與暫緩項目

## 背景

2026-04-04 完成全面 code review（見 `docs/tron-sdk-code-review-2026-04-04.md`），識別出 2 個 Critical、4 個 Warning、5 個 Info 級別問題。

## 決策

### 已修復

| 編號 | 問題 | 修復方式 |
|------|------|----------|
| C-1 | `JsonDocument` 未 dispose，記憶體洩漏 | 12 處加 `using var doc = JsonDocument.Parse(json)` |
| C-2 | `Math.Pow(10, decimals)` double→decimal 精度損失 | 新增 `TronConverter.DecimalPow10` 用 decimal 迴圈乘法，7 處統一替換 |
| W-2 | 金額驗證用了 `InvalidAddress` error code | 新增 `TronErrorCode.InvalidAmount`，13 處替換 |
| W-4 | `HttpClient` 注入 constructor 是 internal | 改 public，加 `solidityUrl` 參數 |
| W-1 | 私鑰 `byte[]` 未清零 | `TronAccount` 加 `IDisposable`，`Dispose()` 用 `CryptographicOperations.ZeroMemory` 清除私鑰 |

### 暫緩

| 編號 | 問題 | 暫緩原因 | 觸發時機 |
|------|------|----------|----------|
| W-3 | 靜默吞異常沒有 log | SDK 無 ILogger 注入機制，需先設計 logging 策略 | 設計 logging 機制時 |
| I-4 | 沒有 `Directory.Build.props` | 目前只有一條鏈 | 新增第二條鏈時 |

### 不採納

| 編號 | 問題 | 原因 |
|------|------|------|
| I-1 | AbiEncoder 重複碼 | 6 個方法各自清楚，抽象化降低可讀性。YAGNI |
| I-2 | TronClient 交易方法重複 | 每個方法有微妙差異，強行抽象降低可讀性 |
| I-3 | 缺少 `ConfigureAwait(false)` | 內部使用，consumer 是 ASP.NET Core，無實際風險 |
| I-5 | 類別未 sealed | JIT 優化在此場景影響微乎其微 |
