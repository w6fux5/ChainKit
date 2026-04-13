# 014: EVM SDK 架構決策

## 背景

ChainKit 需要新增 EVM 相容鏈支援（Ethereum + Polygon）。需要決定：專案命名與範圍、共用元件策略、外部依賴選擇、Watcher 實作方式、測試策略。

## 決策

### 專案命名：`ChainKit.Evm`（非 `ChainKit.Ethereum`）

選擇 `ChainKit.Evm` 作為專案名稱，一個專案涵蓋所有 EVM 相容鏈，用 `EvmNetworkConfig` 的 `ChainId` 區分不同鏈。

**原因：** Polygon 是 EVM 相容鏈，和 Ethereum 共用相同的地址格式（0x）、交易結構（RLP）、簽名演算法（SECP256K1 + Keccak256）、JSON-RPC API、ABI 編碼。差異只在 RPC 端點、Chain ID、Gas 費用、原生幣名稱 — 這些只需要一個 network config 就能處理。

**放棄的方案：** `ChainKit.Ethereum` + 在內部支援 Polygon。命名不精確，暗示「只有 Ethereum」。

### 共用元件策略：搬到 Core

將 Keccak256、AbiEncoder（通用部分）、Mnemonic、TokenConverter 從 `ChainKit.Tron` 搬到 `ChainKit.Core`。

**搬到 Core 的依據（逐檔原始碼分析確認）：**
- `Keccak256` — 純演算法，零鏈特定邏輯，註解自己寫 "Used for Ethereum/Tron"
- `AbiEncoder` — `EncodeFunctionSelector`/`EncodeUint256`/`DecodeUint256`/`DecodeString` 是 Solidity ABI 標準，跨鏈通用。但 `EncodeAddress`/`DecodeAddress` 硬寫 Tron 的 `41` 前綴，因此只搬通用方法，地址方法各鏈自己做（`TronAbiEncoder`/`EvmAbiEncoder`）
- `Mnemonic` — BIP-39 標準，無鏈特定邏輯。BIP-44 路徑由各鏈 Account 處理
- `TokenConverter` — `DecimalPow10`/`ToTokenAmount`/`ToRawAmount` 是通用 token decimals 邏輯

**留在各鏈的：**
- `Signer` — v 值計算不同（Tron: `recovery_id`，EVM: `chainId * 2 + 35 + recovery_id`）
- `Address` — 格式完全不同（Base58 vs 0x checksum）
- `Account` — 地址推導前綴不同（0x41 vs 取後 20 bytes）
- 原生幣轉換 — Sun/TRX vs Wei/ETH

**放棄的方案：**
- 方案 A（完全平行）— 不動 Tron，EVM 自帶一份。消除重複的機會浪費
- 方案 C（用 Nethereum 的 ABI/Keccak）— 引入外部依賴做已有的事

### 外部依賴：不使用 Nethereum

經過完整分析 Nethereum 原始碼（clone 下來逐層檢查），決定不依賴任何 Nethereum 套件。

**分析結果：**

| 功能 | Nethereum 套件 | ChainKit 已有？ | 決定 |
|------|---------------|----------------|------|
| Keccak256 | `Nethereum.Util` | ✅ Core 已有 | 不用 |
| ABI 編碼 | `Nethereum.ABI` | ✅ Core 已有 | 不用 |
| SECP256K1 | `Nethereum.Signer`（用 BouncyCastle） | ✅ 用 `NBitcoin.Secp256k1`（更快） | 不用 |
| Hex 工具 | `Nethereum.Hex` | ✅ Core 已有 | 不用 |
| RLP 編碼 | `Nethereum.RLP` | ❌ 不存在 | 自己寫（~80 行） |

**原因：**
- ChainKit 已有全部底層元件，唯一缺的是 RLP 編碼（規格簡單，自己寫約 80 行）
- 如果引入 `Nethereum.Signer`，會連帶拉入 `Nethereum.Model` → `Nethereum.RLP` + `Nethereum.Util` + `BouncyCastle` 一整串依賴
- `NBitcoin.Secp256k1` 比 BouncyCastle 快 20-100x（ADR 001 已確認）

**放棄的方案：**
- 混合使用 Nethereum 低階套件 — 依賴鏈太長，且與 Core 功能重疊
- 完全使用 Nethereum — 風格與 ChainKit 的 Result Pattern 不合

### Watcher：Polling + WebSocket 雙實作

提供 `PollingBlockStream`（HTTP 輪詢）和 `WebSocketBlockStream`（`eth_subscribe`）兩種實作，使用者建構時選擇。

**原因：** 跟 Tron SDK 的 `PollingBlockStream` + `ZmqBlockStream` 架構對齊。Polling 相容性最好（所有節點都支援），WebSocket 延遲更低。兩者都實作 `IEvmBlockStream` 介面。

### ERC20 Transfer 偵測：receipt logs（非 input data）

使用 `eth_getTransactionReceipt` 的 logs 欄位，解析 `Transfer(address,address,uint256)` event topic（`0xddf252ad...`），取代 Tron 使用的 input data 解析方式。

**原因：** logs 反映實際執行結果（包含合約內部 transfer），input data 只反映外部呼叫（不含 internal calls）。

### 交易確認機制：receipt status + block confirmations

- `eth_getTransactionReceipt` 存在 + `status == "0x1"` + 當前區塊 - 交易區塊 ≥ N（預設 12）= Confirmed
- `status == "0x0"` = Failed

**原因：** 與 Tron 不同（Tron 需要 Solidity Node），EVM 用單一 RPC 端點的 receipt 就能判斷。block confirmation depth 提供 finality 保護。

### 測試策略：Unit + Anvil Integration + Sepolia E2E

| 層級 | 工具 | 用途 |
|------|------|------|
| Unit | NSubstitute mock IEvmProvider | 業務邏輯、編碼、簽名 |
| Integration | Anvil 本地 EVM 節點 | 真實交易流程 |
| E2E（少量） | Sepolia 測試網 | 驗證真實網路行為 |

**原因：** Sepolia ETH 難拿且不穩定。Anvil 提供 10 個預先充值的帳戶（各 10,000 ETH），出塊即時，完全可控。90% 的測試用 Anvil，Sepolia 只驗證「真實網路行為與本地一致」。

### 合約標準：僅 ERC20

與 Tron SDK 的 TRC20 對齊，不支援 ERC721（NFT）或 ERC1155（多代幣）。

**原因：** 內部系統只需要處理標準代幣（USDT、USDC）。ERC721/ERC1155 未來有需要再加。

## 最終架構

```
ChainKit.Core    — IAccount, ITransaction, ChainResult, Keccak256, AbiEncoder, Mnemonic, TokenConverter
ChainKit.Tron    — TronSigner, TronAddress, TronAccount, TronAbiEncoder, TronConverter, Protobuf, ITronProvider...
ChainKit.Evm     — EvmSigner, EvmAddress, EvmAccount, EvmAbiEncoder, RlpEncoder, IEvmProvider...
```
