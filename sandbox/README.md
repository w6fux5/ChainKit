# ChainKit Sandbox

Tron SDK 的 Web API 測試介面，將 SDK 的所有公開方法對應為 HTTP endpoint，方便用 Scalar UI 或 HTTP client 直接調試。

## 啟動

```bash
dotnet run --project sandbox/ChainKit.Sandbox
```

啟動後開啟瀏覽器：

| 介面 | URL |
|------|-----|
| Scalar UI（推薦） | http://localhost:5178/scalar/v1 |
| Swagger UI | http://localhost:5178/swagger |

## 設定

編輯 `appsettings.json` 切換網路和 Provider：

```json
{
  "Tron": {
    "HttpEndpoint": "https://nile.trongrid.io",
    "HttpSolidityEndpoint": "",
    "GrpcFullNodeEndpoint": "",
    "GrpcSolidityEndpoint": "",
    "ApiKey": "",
    "ZmqEndpoint": "",
    "Watcher": {
      "PollingIntervalMs": 3000,
      "ConfirmationIntervalMs": 3000,
      "MaxPendingAgeMinutes": 5
    }
  }
}
```

| 設定 | 說明 | 預設值 |
|------|------|--------|
| `HttpEndpoint` | HTTP Full Node 端點 | `https://nile.trongrid.io`（Nile 測試網） |
| `HttpSolidityEndpoint` | HTTP Solidity Node 端點（留空則同 HttpEndpoint） | 空 |
| `GrpcFullNodeEndpoint` | gRPC Full Node 端點（填入後優先使用 gRPC） | 空 |
| `GrpcSolidityEndpoint` | gRPC Solidity Node 端點 | 空 |
| `ApiKey` | TronGrid API Key | 空 |
| `ZmqEndpoint` | ZMQ 端點（填入後 Watcher 使用 ZMQ 而非 Polling） | 空 |
| `Watcher.PollingIntervalMs` | Polling 模式輪詢間隔 | 3000 |
| `Watcher.ConfirmationIntervalMs` | 確認追蹤間隔 | 3000 |
| `Watcher.MaxPendingAgeMinutes` | 未確認交易最大等待時間 | 5 |

### 切換網路

```json
// Nile 測試網（預設）
"HttpEndpoint": "https://nile.trongrid.io"

// Mainnet
"HttpEndpoint": "https://api.trongrid.io"

// Shasta 測試網
"HttpEndpoint": "https://api.shasta.trongrid.io"
```

### 切換為 gRPC Provider

填入 `GrpcFullNodeEndpoint` 後，Sandbox 會優先使用 gRPC：

```json
"GrpcFullNodeEndpoint": "grpc.nile.trongrid.io:50051",
"GrpcSolidityEndpoint": "grpc.nile.trongrid.io:50061"
```

---

## API 與 SDK 對照表

Sandbox 的每個 endpoint 直接對應 SDK 方法，請求參數和回傳結構與 SDK 一致。

### Wallet — `TronAccount` / `TronAddress`

| Endpoint | SDK 方法 | 回傳型別 |
|----------|----------|----------|
| `POST /api/wallet/create` | `TronAccount.Create()` | `{ address, hexAddress, publicKey, privateKey }` |
| `POST /api/wallet/from-mnemonic` | `TronAccount.FromMnemonic(mnemonic, index)` | 同上 |
| `POST /api/wallet/from-private-key` | `TronAccount.FromPrivateKey(privateKey)` | `{ address, hexAddress, publicKey }` |
| `GET /api/wallet/validate/{address}` | `TronAddress.IsValid(address)` | `bool` |
| `GET /api/wallet/address/to-base58/{hex}` | `TronAddress.ToBase58(hex)` | `string` |
| `GET /api/wallet/address/to-hex/{base58}` | `TronAddress.ToHex(base58)` | `string` |

### Account — `TronClient` 查詢 + `ITronProvider` 低階

| Endpoint | SDK 方法 | 回傳型別 |
|----------|----------|----------|
| `GET /api/account/{address}/balance?trc20=...` | `TronClient.GetBalanceAsync(address, trc20Contracts)` | `BalanceInfo` |
| `GET /api/account/{address}/resources` | `TronClient.GetResourceInfoAsync(address)` | `ResourceInfo` |
| `GET /api/account/{address}/raw` | `ITronProvider.GetAccountAsync(address)` | `AccountInfo` |
| `GET /api/account/{address}/raw-resource` | `ITronProvider.GetAccountResourceAsync(address)` | `AccountResourceInfo` |
| `GET /api/account/{address}/transactions?limit=10` | `ITronProvider.GetAccountTransactionsAsync(address, limit)` | `TransactionInfoDto[]` |

### Transfer — `TronClient` 轉帳

| Endpoint | SDK 方法 | 回傳型別 |
|----------|----------|----------|
| `POST /api/transfer/trx` | `TronClient.TransferTrxAsync(account, toAddress, amount)` | `TransferResult` |

**Request body：**
```json
{ "privateKey": "hex...", "toAddress": "T...", "amount": 10.0 }
```

### TRC20 — `TronClient` 高階 + `Trc20Contract` 低階

| Endpoint | SDK 方法 | 回傳型別 |
|----------|----------|----------|
| `POST /api/trc20/transfer` | `TronClient.TransferTrc20Async(account, contract, to, amount, decimals)` | `TransferResult` |
| `GET /api/trc20/{contract}/name` | `Trc20Contract.NameAsync()` | `string` |
| `GET /api/trc20/{contract}/symbol` | `Trc20Contract.SymbolAsync()` | `string` |
| `GET /api/trc20/{contract}/decimals` | `Trc20Contract.DecimalsAsync()` | `byte` |
| `GET /api/trc20/{contract}/total-supply` | `Trc20Contract.TotalSupplyAsync()` | `decimal` |
| `GET /api/trc20/{contract}/balance-of/{owner}` | `Trc20Contract.BalanceOfAsync(address)` | `decimal` |
| `GET /api/trc20/{contract}/allowance/{owner}/{spender}` | `Trc20Contract.AllowanceAsync(owner, spender)` | `decimal` |
| `POST /api/trc20/contract-transfer` | `Trc20Contract.TransferAsync(to, amount)` | `TransferResult` |
| `POST /api/trc20/approve` | `Trc20Contract.ApproveAsync(spender, amount)` | `TransferResult` |
| `POST /api/trc20/mint` | `Trc20Contract.MintAsync(to, amount)` | `TransferResult` |
| `POST /api/trc20/burn` | `Trc20Contract.BurnAsync(amount)` | `TransferResult` |
| `POST /api/trc20/burn-from` | `Trc20Contract.BurnFromAsync(from, amount)` | `TransferResult` |

### Staking — `TronClient` 資源管理 + `ITronProvider` 低階

| Endpoint | SDK 方法 | 回傳型別 |
|----------|----------|----------|
| `POST /api/staking/stake` | `TronClient.StakeTrxAsync(account, amount, resource)` | `StakeResult` |
| `POST /api/staking/unstake` | `TronClient.UnstakeTrxAsync(account, amount, resource)` | `UnstakeResult` |
| `POST /api/staking/delegate` | `TronClient.DelegateResourceAsync(account, receiver, amount, resource, lock)` | `DelegateResult` |
| `POST /api/staking/undelegate` | `TronClient.UndelegateResourceAsync(account, receiver, amount, resource)` | `UndelegateResult` |
| `GET /api/staking/delegation/{address}` | `ITronProvider.GetDelegatedResourceAccountIndexAsync(address)` | 委託索引 |
| `GET /api/staking/delegation/{from}/to/{to}` | `ITronProvider.GetDelegatedResourceAsync(from, to)` | 委託詳情 |

### Contract — `TronClient` 部署 + `ITronProvider` 低階

| Endpoint | SDK 方法 | 回傳型別 |
|----------|----------|----------|
| `POST /api/contract/deploy` | `TronClient.DeployContractAsync(account, bytecode, abi, feeLimit)` | `DeployResult` |
| `POST /api/contract/deploy-trc20` | `TronClient.DeployTrc20TokenAsync(account, options)` | `DeployResult` |
| `POST /api/contract/call` | `ITronProvider.TriggerConstantContractAsync(owner, contract, selector, param)` | `string`（hex） |
| `POST /api/contract/estimate-energy` | `ITronProvider.EstimateEnergyAsync(owner, contract, selector, param)` | `long` |

### Transaction — `TronClient` 高階 + `ITronProvider` 低階

| Endpoint | SDK 方法 | 回傳型別 |
|----------|----------|----------|
| `GET /api/transaction/{txId}` | `TronClient.GetTransactionDetailAsync(txId)` | `TronTransactionDetail` |
| `GET /api/transaction/{txId}/raw` | `ITronProvider.GetTransactionByIdAsync(txId)` | Full Node 原始資料 |
| `GET /api/transaction/{txId}/info` | `ITronProvider.GetTransactionInfoByIdAsync(txId)` | Solidity Node 原始資料 |

### Block — `ITronProvider`

| Endpoint | SDK 方法 | 回傳型別 |
|----------|----------|----------|
| `GET /api/block/latest` | `ITronProvider.GetNowBlockAsync()` | `BlockInfo` |
| `GET /api/block/{num}` | `ITronProvider.GetBlockByNumAsync(num)` | `BlockInfo` |

### Watcher — `TronTransactionWatcher`

| Endpoint | SDK 方法 | 說明 |
|----------|----------|------|
| `POST /api/watcher/watch/{address}` | `WatchAddress(address)` | 新增單一監聽地址 |
| `POST /api/watcher/watch` | `WatchAddresses(addresses)` | 批量新增監聽地址 |
| `DELETE /api/watcher/watch/{address}` | `UnwatchAddress(address)` | 移除監聯地址 |
| `GET /api/watcher/events` | SSE 串流 | 即時推送所有 watcher 事件 |

Watcher 隨 Sandbox 啟動自動運行（BackgroundService），透過 SSE 推送六種事件：

| 事件 | SDK 事件 | 觸發時機 |
|------|----------|----------|
| `TrxReceived` | `OnTrxReceived` | 監聽地址收到 TRX |
| `TrxSent` | `OnTrxSent` | 監聽地址轉出 TRX |
| `Trc20Received` | `OnTrc20Received` | 監聽地址收到 TRC20 |
| `Trc20Sent` | `OnTrc20Sent` | 監聯地址轉出 TRC20 |
| `Confirmed` | `OnTransactionConfirmed` | 交易被 Solidity Node 確認 |
| `Failed` | `OnTransactionFailed` | 交易失敗或確認逾時 |

---

## 測試流程範例

### 1. 建立測試帳戶並取得測試幣

```bash
# 建立新帳戶
curl -X POST http://localhost:5178/api/wallet/create

# 用回傳的地址到 Nile Faucet 領測試幣
# https://nileex.io/join/getJoinPage
```

### 2. 查詢餘額

```bash
# 只查 TRX
curl http://localhost:5178/api/account/{address}/balance

# 同時查 TRX + TRC20
curl "http://localhost:5178/api/account/{address}/balance?trc20=TF17BgPaZYbz8oxbjhriubPDsA7ArKoLX3"
```

### 3. TRX 轉帳

```bash
curl -X POST http://localhost:5178/api/transfer/trx \
  -H "Content-Type: application/json" \
  -d '{"privateKey":"your_hex_key","toAddress":"TRecipient...","amount":1.5}'
```

### 4. 查詢交易狀態

```bash
# 高階（合併 Full Node + Solidity Node）
curl http://localhost:5178/api/transaction/{txId}

# 低階：Full Node 原始資料
curl http://localhost:5178/api/transaction/{txId}/raw

# 低階：Solidity Node 確認資料
curl http://localhost:5178/api/transaction/{txId}/info
```

### 5. TRC20 操作

```bash
# 查代幣資訊
curl http://localhost:5178/api/trc20/{contractAddress}/name
curl http://localhost:5178/api/trc20/{contractAddress}/decimals

# 查餘額
curl http://localhost:5178/api/trc20/{contractAddress}/balance-of/{ownerAddress}

# 轉帳（高階，需要指定 decimals）
curl -X POST http://localhost:5178/api/trc20/transfer \
  -H "Content-Type: application/json" \
  -d '{"privateKey":"hex...","contractAddress":"T...","toAddress":"T...","amount":100,"decimals":6}'
```

### 6. 監聽交易

```bash
# 註冊監聽地址
curl -X POST http://localhost:5178/api/watcher/watch/{address}

# 開啟 SSE 連線接收事件（保持連線）
curl -N http://localhost:5178/api/watcher/events
```

SSE 輸出範例：

```
data: {"Event":"TrxReceived","TxId":"abc...","FromAddress":"T...","ToAddress":"T...","Amount":10.5,"BlockNumber":12345}

data: {"Event":"Confirmed","TxId":"abc...","BlockNumber":12345}
```

### 7. 部署 TRC20 代幣

```bash
curl -X POST http://localhost:5178/api/contract/deploy-trc20 \
  -H "Content-Type: application/json" \
  -d '{"privateKey":"hex...","name":"Test Token","symbol":"TTK","decimals":6,"initialSupply":1000000}'
```

### 8. 資源管理

```bash
# 查資源狀態
curl http://localhost:5178/api/account/{address}/resources

# 質押（Resource: 0=Bandwidth, 1=Energy）
curl -X POST http://localhost:5178/api/staking/stake \
  -H "Content-Type: application/json" \
  -d '{"privateKey":"hex...","trxAmount":100,"resource":1}'
```

---

## 回傳格式

### 成功

SDK 方法回傳 `TronResult<T>` 時，Sandbox 直接回傳 `T`（HTTP 200）：

```json
// GET /api/account/{address}/balance
{
  "trxBalance": 8967.36755,
  "trc20Balances": {
    "TF17BgPaZYbz8oxbjhriubPDsA7ArKoLX3": {
      "rawBalance": 9999998800000000,
      "balance": 9999998800,
      "symbol": "TTTU",
      "decimals": 6
    }
  }
}

// GET /api/trc20/{contract}/symbol
"USDT"

// GET /api/trc20/{contract}/decimals
6
```

### 失敗

SDK 方法回傳 `TronResult.Fail` 時，Sandbox 回傳 `TronError`（HTTP 400）：

```json
{
  "code": "InsufficientBalance",
  "message": "TRX 餘額不足",
  "rawMessage": "..."
}
```
