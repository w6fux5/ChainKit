# ChainKit

多鏈 SDK，供內部系統使用。目前支援的鏈：Tron

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
- `sandbox/ChainKit.Sandbox/` — Web API 測試介面（Scalar UI），串接所有 SDK API

## 指令

- 環境需求：.NET 10 SDK、npm（Solidity 編譯用）
- Build: `dotnet build`
- Unit tests: `dotnet test --filter "Category!=Integration"`
- E2E tests: `dotnet test --filter "Category=Integration"`
- All tests: `dotnet test`
- Sandbox: `dotnet run --project sandbox/ChainKit.Sandbox`（Scalar UI: http://localhost:5178/scalar/v1）
- Sandbox（背景啟動）: `dotnet run --project sandbox/ChainKit.Sandbox -- --urls "http://localhost:5178" &`
- Coverage: `dotnet test --filter "Category!=Integration" --collect:"XPlat Code Coverage" --results-directory ./coverage-results && reportgenerator -reports:"coverage-results/*/coverage.cobertura.xml" -targetdir:coverage-report -reporttypes:TextSummary && cat coverage-report/Summary.txt`

### E2E 測試環境變數（可選，有預設值）

- `TRON_TEST_PRIVATE_KEY_1` — Nile 測試帳戶1 私鑰
- `TRON_TEST_PRIVATE_KEY_2` — Nile 測試帳戶2 私鑰

## 慣例

- 命名：PascalCase（public）、_camelCase（private field）
- 所有 public API 需有 XML doc comment
- 高階 API 回傳 `TronResult<T>`，業務錯誤不 throw，只有 SDK 內部 bug 才 throw
- Token 金額同時提供 RawAmount（永遠正確）+ Amount?（null = 無法轉換）
- TRX 金額：高階用 decimal TRX，低階用 long Sun（1 TRX = 1,000,000 Sun）
- 所有金額輸入驗證正數 + overflow 保護
- 金額計算用 `TronConverter.DecimalPow10`（decimal 迴圈乘法），禁用 `Math.Pow`（double 精度損失）
- `JsonDocument.Parse` 必須用 `using` dispose（歸還 ArrayPool 記憶體）
- IDisposable：TronClient、TronHttpProvider、TronGrpcProvider、Trc20Contract、TronAccount（清零私鑰）
- IAsyncDisposable：TronTransactionWatcher
- Thread safe：TokenInfoCache（ConcurrentDictionary）、Trc20Contract（SemaphoreSlim）、Watcher（lock）
- Watcher 六事件：OnTrx/Trc20 Received/Sent（Unconfirmed）+ OnTransactionConfirmed/Failed（Solidity Node 確認）
- Watcher 事件的地址欄位統一回傳 Base58 格式（T 開頭），provider 層保持 hex（見 ADR 007）
- TronHttpProvider 支援雙端點：`baseUrl`（Full Node）+ `solidityUrl`（Solidity Node，預設同 baseUrl）。未設定 Solidity Node 時無法正確判斷交易確認狀態（見 ADR 006）
- 新增功能必須有對應測試
- 測試 mock 資料必須模擬真實節點回應（如空物件 `{}`），不能只配合程式邏輯設計 mock
- 高階 API 保持單一職責，不做 aggregate 端點（查餘額、查資源、查交易分開，見 ADR 008）
- 新增鏈遵循相同架構：`ChainKit.{Chain}` + 共用 `ChainKit.Core`

## Tron 開發注意事項

- Broadcast 交易用 `/wallet/broadcasthex`（hex encoded protobuf），不要用 `/wallet/broadcasttransaction`（JSON 格式會被 Tron 節點的 fastjson 拒絕）
- TAPOS ref block hash 取 block ID 的 bytes 8-15（不是 0-7）
- TriggerSmartContract 回傳的交易直接用 `raw_data_hex` 解析，不要 JSON→protobuf→重新序列化（位元組會不一致導致 SIGERROR）
- Protobuf proto 檔案放在 `Protocol/Protobuf/` 扁平目錄，.csproj 用 `ProtoRoot="Protocol\Protobuf"`
- Solidity 編譯：`npm install -g solc` → `solcjs --bin --abi --optimize`
- E2E 測試帳戶 bandwidth 會耗盡，測試需要有 graceful degradation（檢測 BANDWITH_ERROR）
- .NET 內建 SHA3_256 是 NIST SHA3（padding 0x06），不是 Tron/Ethereum 的 Keccak-256（padding 0x01），不能混用
- 自建節點：HTTP（8090/8091）預設開啟，gRPC（50051/50061）需在 config.conf 的 `node.rpc` 手動開啟
- 交易確認判斷必須用 Solidity Node（`/walletsolidity/`），Full Node 不處理此路徑（回傳 405）
- Smart Contract 確認：`receipt.result == SUCCESS`；System Contract：Solidity Node 查得到即 confirmed
- Solidity Node 回傳 `{}` 代表交易未確認，解析時不可用呼叫端的 txId 填充（見 ADR 006）
- Tron HTTP API 端點名稱**大小寫敏感**（如 `v2` 不能寫成 `V2`），錯誤時回傳 405（見 ADR 009）
- `TronHttpProvider` 全域用 `SnakeCaseLower` 序列化，新增 API 時注意 request body key 是否會被轉換（見 ADR 009）

## 關鍵設計決策

- 單套件 per 鏈（內部使用，不拆多套件）
- Result Pattern（不用 Exception 處理業務錯誤）
- 三層 Token Info Cache（內建表 → memory cache → 合約呼叫）
- 交易狀態三態（Unconfirmed/Confirmed/Failed），確認機制見 ADR 006
- Watcher 雙向監聽 + 三階段生命週期，見 ADR 007 和 `docs/superpowers/specs/2026-04-04-watcher-lifecycle-design.md`
- 詳見 `docs/decisions/001-tron-sdk-architecture.md`

## 文件

- `docs/tron-sdk-usage-guide.md` — 使用指南（安裝、範例、高低階 API、工具類、錯誤處理）
- `docs/decisions/` — 架構決策紀錄：
  - 001 Tron SDK 整體架構
  - 002 移除 TransactionStatus.NotFound
  - 003 HTTP Provider 雙端點設計
  - 004 FailureReason 對齊 Tron receipt
  - 005 Code Review 修復與暫緩項目
  - 006 交易確認狀態判斷修正（ParseTransactionInfo 空物件處理）
  - 007 Watcher 事件地址格式統一為 Base58
  - 008 移除 GetAccountOverviewAsync（職責重疊、狀態判斷不一致）
  - 009 Delegation API 端點修正（大小寫、JSON key 序列化）
- `docs/tron-sdk-development-summary.md` — 開發總結
- `docs/tron-transaction-lifecycle.md` — 交易生命週期（階段、狀態對應、Watcher 功能）
- `docs/superpowers/specs/2026-04-03-tron-sdk-design.md` — 設計規格（初版，部分內容已更新）
- `docs/superpowers/specs/2026-04-04-watcher-lifecycle-design.md` — Watcher 生命週期增強設計
- `docs/tron-sdk-code-review-2026-04-04.md` — Code Review 報告
- `docs/superpowers/plans/` — 實作計畫（4 份）
- `sandbox/README.md` — Sandbox 使用說明（啟動、設定、API 對照表、測試流程）
