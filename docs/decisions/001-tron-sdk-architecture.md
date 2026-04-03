# 001: Tron SDK 架構決策

## 背景

需要為內部系統開發 Tron 區塊鏈 SDK，支援地址管理、交易、TRC20 代幣、質押/委託、合約部署、交易監聽。

## 決策

### 專案結構：單套件 per 鏈

選擇 `ChainKit.Core` + `ChainKit.Tron` 兩個套件，而非每層一個套件（Core/Protocol/Client/Facade）。

**原因：** 內部使用，不需要讓外部用戶按需引用。一個套件降低維護成本，內部用 namespace 分層即可。

**放棄的方案：** 5 個獨立 NuGet 套件的分層架構。適合開源但對內部使用太重。

### 錯誤處理：Result Pattern + RawAmount

- 業務錯誤用 `TronResult<T>`，不 throw exception
- Token 金額同時回傳 `RawAmount`（永遠正確）和 `Amount?`（轉換後，null = 無法轉換）
- 只有 SDK 內部 bug 才 throw

**原因：** 用戶不需要 try-catch，`RawAmount` 保證百分百不會回傳錯誤數字。

**放棄的方案：**
- 多層 Exception 類別 — 增加用戶負擔
- 只回傳轉換後金額 — 無法保證正確性

### 加密庫：NBitcoin.Secp256k1 + 自行實作 Keccak-256

**原因：**
- NBitcoin.Secp256k1：Nethereum/TronNet 都用，比 BouncyCastle 快 20-100x
- Keccak-256 自行實作：避免為一個 hash 拉整個 BouncyCastle

**放棄的方案：**
- BouncyCastle：太重，EC 效能差
- Secp256k1.Net：需要 native binary

### Token 資訊解析：三層策略

1. 內建已知代幣表（USDT 等）→ 零延遲
2. Memory cache → 零延遲
3. 合約呼叫 symbol() + decimals() → 首次需網路，結果永久 cache

**原因：** 大部分場景用 USDT，不需要網路呼叫。冷門代幣只查一次。

### 交易狀態判斷：Full Node + Solidity Node 配合

- `getTransactionById`（Full Node）→ 交易本體
- `getTransactionInfoById`（Solidity Node）→ 回執（status、fee、energy、event log）
- 四態：NotFound / Unconfirmed / Confirmed / Failed

**原因：** Tron 的確認機制需要兩個 API 配合。HTTP Provider 透過同一個 URL 存取兩者。gRPC Provider 的 Solidity endpoint 為 optional。

### 交易監聽：ITronBlockStream 抽象

- PollingBlockStream：輪詢 API，不需節點，適合少量地址
- ZmqBlockStream：ZMQ 推送，需自架節點，適合大量地址
- TronTransactionWatcher：統一介面，HashSet O(1) 地址過濾

**原因：** 不同用戶有不同基礎設施，抽象讓兩種方案可互換。
