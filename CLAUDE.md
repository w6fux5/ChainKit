# ChainKit

多鏈 SDK，供內部系統使用。目前支援的鏈：Tron（production ready）

## 技術棧

- .NET 10.0
- C#（Nullable enabled, ImplicitUsings enabled）
- Solution: ChainKit.slnx
- 測試框架：xUnit + NSubstitute

## 專案結構

- `src/ChainKit.Core/` — 跨鏈共用（ChainResult、IAccount、ITransaction、Hex/Base58 工具）
- `src/ChainKit.Tron/` — Tron SDK
  - `Crypto/` — TronAccount、Mnemonic、TronAddress、TronSigner、AbiEncoder、Keccak256、TronConverter
  - `Protocol/` — Protobuf 定義、TransactionBuilder、TransactionUtils
  - `Providers/` — ITronProvider、TronHttpProvider、TronGrpcProvider、TronNetwork
  - `Contracts/` — Trc20Contract、Trc20Template、TokenInfoCache
  - `Watching/` — ITronBlockStream、PollingBlockStream、ZmqBlockStream、TronTransactionWatcher
  - `Models/` — TronResult、所有 DTO、Enums
  - `TronClient.cs` — 高階 Facade
- `tests/ChainKit.Core.Tests/` — Core 單元測試
- `tests/ChainKit.Tron.Tests/` — Tron 單元測試 + E2E 測試
- `contracts/` — Solidity 原始碼和編譯輸出（TRC20 模板）

## 指令

- Build: `dotnet build`
- Unit tests: `dotnet test --filter "Category!=Integration"`
- E2E tests: `dotnet test --filter "Category=Integration"`
- All tests: `dotnet test`

## 慣例

- 命名：PascalCase（public）、_camelCase（private field）
- 所有 public API 需有 XML doc comment
- 高階 API 回傳 `TronResult<T>`，業務錯誤不 throw，只有 SDK 內部 bug 才 throw
- Token 金額同時提供 RawAmount（永遠正確）+ Amount?（null = 無法轉換）
- TRX 金額：高階用 decimal TRX，低階用 long Sun（1 TRX = 1,000,000 Sun）
- 所有金額輸入驗證正數 + overflow 保護
- IDisposable：TronClient、TronHttpProvider、TronGrpcProvider、Trc20Contract
- Thread safe：TokenInfoCache（ConcurrentDictionary）、Trc20Contract（SemaphoreSlim）、Watcher（lock）
- 新增功能必須有對應測試
- 新增鏈遵循相同架構：`ChainKit.{Chain}` + 共用 `ChainKit.Core`

## Tron 開發注意事項

- Broadcast 交易用 `/wallet/broadcasthex`（hex encoded protobuf），不要用 `/wallet/broadcasttransaction`（JSON 格式會被 Tron 節點的 fastjson 拒絕）
- TAPOS ref block hash 取 block ID 的 bytes 8-15（不是 0-7）
- TriggerSmartContract 回傳的交易直接用 `raw_data_hex` 解析，不要 JSON→protobuf→重新序列化（位元組會不一致導致 SIGERROR）
- Protobuf proto 檔案放在 `Protocol/Protobuf/` 扁平目錄，.csproj 用 `ProtoRoot="Protocol\Protobuf"`
- Solidity 編譯：`npm install -g solc` → `solcjs --bin --abi --optimize`
- E2E 測試帳戶 bandwidth 會耗盡，測試需要有 graceful degradation（檢測 BANDWITH_ERROR）
- .NET 內建 SHA3_256 是 NIST SHA3（padding 0x06），不是 Tron/Ethereum 的 Keccak-256（padding 0x01），不能混用

## 關鍵設計決策

- 單套件 per 鏈（內部使用，不拆多套件）
- Result Pattern（不用 Exception 處理業務錯誤）
- 三層 Token Info Cache（內建表 → memory cache → 合約呼叫）
- 交易狀態四態（NotFound/Unconfirmed/Confirmed/Failed，需 Full Node + Solidity Node）
- 詳見 `docs/decisions/001-tron-sdk-architecture.md`

## 文件

- `docs/tron-sdk-usage-guide.md` — 使用指南（安裝、範例、高低階 API、工具類、錯誤處理）
- `docs/decisions/001-tron-sdk-architecture.md` — 架構決策紀錄
- `docs/tron-sdk-development-summary.md` — 開發總結
- `docs/superpowers/specs/2026-04-03-tron-sdk-design.md` — 設計規格（與實作同步）
- `docs/superpowers/plans/` — 實作計畫（3 份）
