# ChainKit

多鏈 SDK，供內部系統使用。目前支援的鏈：Tron

## 技術棧

- .NET 10.0
- C#（Nullable enabled, ImplicitUsings enabled）
- Solution: ChainKit.slnx

## 專案結構

- `src/ChainKit.Core/` — 跨鏈共用（Result Pattern、介面、工具）
- `src/ChainKit.Tron/` — Tron SDK（Crypto、Protocol、Providers、Contracts、Watching、Models）
- `tests/ChainKit.Core.Tests/` — Core 測試
- `tests/ChainKit.Tron.Tests/` — Tron 測試
- `contracts/` — Solidity 原始碼和編譯輸出

## 指令

- Build: `dotnet build`
- Test: `dotnet test`

## 慣例

- 語言：C#
- 命名：PascalCase（public）、_camelCase（private field）
- 所有 public API 需有 XML doc comment
- 高階 API 回傳 `TronResult<T>`，業務錯誤不 throw
- Token 金額同時提供 RawAmount + Amount?

## 文件

- `docs/decisions/` — 架構決策紀錄（ADR）
- `docs/tron-sdk-development-summary.md` — Tron SDK 開發總結
- `docs/superpowers/specs/` — 設計規格
- `docs/superpowers/plans/` — 實作計畫
