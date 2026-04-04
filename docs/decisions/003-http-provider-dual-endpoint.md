# 003: TronHttpProvider 支援雙端點

## 背景

Tron 節點分為 Full Node（處理交易、查區塊）和 Solidity Node（查已確認交易）。使用 TronGrid 時，單一 URL 同時提供兩者的 API（`/wallet/...` 和 `/walletsolidity/...`）。但自建節點通常是兩台不同的機器或不同的 port：

- Full Node: `http://your-server:8090`
- Solidity Node: `http://your-server:8091`

原本 `TronHttpProvider` 只接受一個 `baseUrl`，所有請求（包含 Solidity Node 的 `/walletsolidity/...`）都打到同一個 URL。這在自建節點場景下無法正確查詢已確認交易。

`TronGrpcProvider` 已經支援分離端點（`fullNodeEndpoint` + `solidityEndpoint`），HTTP 版本需要對齊。

## 決策

- `TronHttpProvider` 建構子新增 `solidityUrl` 參數（optional，預設 null）
- `solidityUrl` 為 null 時，使用 `baseUrl`（與 TronGrid 相容，行為不變）
- `solidityUrl` 有值時，`/walletsolidity/...` 的請求走 `solidityUrl`
- 內部新增 `PostSolidityAsync` 方法，專門處理 Solidity Node 請求

## 建構子簽章

```csharp
public TronHttpProvider(string baseUrl, string? solidityUrl = null, string? apiKey = null)
```

## 使用方式

```csharp
// TronGrid（單一 URL，向後相容）
var provider = new TronHttpProvider("https://nile.trongrid.io");

// 自建節點（分離端點）
var provider = new TronHttpProvider("http://your-server:8090", "http://your-server:8091");
```

## 原因

1. **自建節點需求** — Full Node 和 Solidity Node 通常是不同的服務
2. **向後相容** — `solidityUrl` 預設 null，現有使用者不受影響
3. **與 gRPC 對齊** — `TronGrpcProvider` 已支援 `solidityEndpoint` 參數
4. **Watcher 確認追蹤依賴 Solidity Node** — 新的確認追蹤器需要查詢 Solidity Node，必須確保 HTTP provider 能正確路由

## 放棄的方案

**改用 `TronNetworkConfig` 加入 `HttpSolidityEndpoint` 欄位：** 需要修改共用的 config record，影響範圍較大。直接在建構子加參數更簡單直接。
