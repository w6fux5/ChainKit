# ChainKit EVM SDK 使用指南

## 安裝

```bash
dotnet add package W6fux5.ChainKit.Evm
```

SDK 會自動引入 `W6fux5.ChainKit.Core` 核心套件。

---

## 快速開始

```csharp
using ChainKit.Evm;
using ChainKit.Evm.Crypto;
using ChainKit.Evm.Providers;

using var provider = new EvmHttpProvider(EvmNetwork.Sepolia);     // 測試網
using var client = new EvmClient(provider, EvmNetwork.Sepolia);
var account = EvmAccount.FromPrivateKey(Convert.FromHexString("你的私鑰hex"));
var result = await client.TransferAsync(account, "0xReceiverAddress...", 0.1m);
Console.WriteLine(result.Success ? $"成功！TxHash: {result.Data!.TxId}" : $"失敗：{result.Error!.Message}");
```

---

## 帳戶管理（EvmAccount）

### 建立方式

```csharp
// 1. 隨機產生新帳戶
var account = EvmAccount.Create();

// 2. 從私鑰匯入
var account = EvmAccount.FromPrivateKey(Convert.FromHexString("your_private_key_hex"));

// 3. 從助記詞推導（BIP-44 路徑：m/44'/60'/0'/0/{index}）
var account = EvmAccount.FromMnemonic("twelve word mnemonic phrase ...", index: 0);
```

### 屬性

| 屬性 | 型別 | 說明 |
|------|------|------|
| `Address` | `string` | 0x-prefixed EIP-55 checksum 地址 |
| `PublicKey` | `byte[]` | 33 bytes compressed public key |
| `PrivateKey` | `byte[]` | 32 bytes private key |

### 安全

`EvmAccount` 實作 `IDisposable`，Dispose 時會清零 PrivateKey：

```csharp
using var account = EvmAccount.FromPrivateKey(key);
// 使用完畢後自動清零私鑰
```

### 地址工具（EvmAddress）

```csharp
// 驗證地址格式
bool valid = EvmAddress.IsValid("0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045"); // true
bool invalid = EvmAddress.IsValid("0x123"); // false

// EIP-55 checksum 轉換
string checksum = EvmAddress.ToChecksumAddress("0xd8da6bf26964af9d7eed9e03e53415d37aa96045");
// → "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045"

// 從公鑰推導地址
string address = EvmAddress.FromPublicKey(uncompressedPublicKey);
```

---

## 網路設定（EvmNetwork）

### 預設網路

| 名稱 | 設定 | Chain ID | 原生幣 |
|------|------|----------|--------|
| Ethereum Mainnet | `EvmNetwork.EthereumMainnet` | 1 | ETH |
| Sepolia（測試網） | `EvmNetwork.Sepolia` | 11155111 | ETH |
| Polygon | `EvmNetwork.PolygonMainnet` | 137 | POL |
| Polygon Amoy（測試網） | `EvmNetwork.PolygonAmoy` | 80002 | POL |

### 自訂網路

```csharp
var network = EvmNetwork.Custom(
    rpcUrl: "https://your-rpc-endpoint.com",
    chainId: 42161,
    name: "Arbitrum One",
    nativeCurrency: "ETH",
    decimals: 18);
```

### Provider 建構

```csharp
// 從預設網路
using var provider = new EvmHttpProvider(EvmNetwork.Sepolia);

// 從自訂 URL
using var provider = new EvmHttpProvider("https://your-rpc-endpoint.com");

// 帶 Logger
using var provider = new EvmHttpProvider(EvmNetwork.Sepolia, logger);
```

---

## 高階 API（EvmClient）

### 建構

```csharp
using var provider = new EvmHttpProvider(EvmNetwork.Sepolia);
using var client = new EvmClient(provider, EvmNetwork.Sepolia, logger);
```

| 參數 | 型別 | 說明 |
|------|------|------|
| `provider` | `IEvmProvider` | JSON-RPC provider（外部擁有，EvmClient 不會 Dispose） |
| `network` | `EvmNetworkConfig` | 網路設定（chain ID、原生幣、decimals） |
| `logger` | `ILogger<EvmClient>?` | 可選，預設 NullLogger |

### TransferAsync — 原生幣轉帳

```csharp
var result = await client.TransferAsync(account, "0xReceiverAddress...", 1.5m);
if (result.Success)
    Console.WriteLine($"TxHash: {result.Data!.TxId}");
else
    Console.WriteLine($"Error: {result.Error!.Message}");
```

| 參數 | 型別 | 說明 |
|------|------|------|
| `from` | `EvmAccount` | 發送者帳戶（提供私鑰簽名） |
| `toAddress` | `string` | 接收者地址（0x） |
| `amount` | `decimal` | 金額，單位為 ETH/POL（非 Wei）。必須正數 |
| `ct` | `CancellationToken` | 可選取消 token |

**回傳：** `EvmResult<TransferResult>`，`TransferResult.TxId` 為交易 hash。

**流程：** 驗證金額 → 轉換 ETH→Wei → 取 nonce → 估算 gas → 取 EIP-1559 費用 → 簽名 → 廣播

### GetBalanceAsync — 查餘額

```csharp
var result = await client.GetBalanceAsync("0xAddress...");
Console.WriteLine($"ETH: {result.Data!.Balance}");      // decimal (如 1.5)
Console.WriteLine($"Wei: {result.Data.RawBalance}");     // BigInteger
```

**回傳：** `EvmResult<BalanceInfo>`

| 欄位 | 型別 | 說明 |
|------|------|------|
| `Balance` | `decimal` | 原生幣金額（ETH/POL） |
| `RawBalance` | `BigInteger` | Wei 金額（永遠正確） |

### GetTransactionDetailAsync — 查交易

```csharp
var result = await client.GetTransactionDetailAsync("0xTxHash...");
var tx = result.Data!;
Console.WriteLine($"Status: {tx.Status}");        // Unconfirmed / Confirmed / Failed
Console.WriteLine($"From: {tx.FromAddress}");
Console.WriteLine($"To: {tx.ToAddress}");
Console.WriteLine($"Amount: {tx.Amount} ETH");
Console.WriteLine($"Fee: {tx.Fee} ETH");
Console.WriteLine($"Block: {tx.BlockNumber}");
```

**回傳：** `EvmResult<EvmTransactionDetail>`

| 欄位 | 型別 | 說明 |
|------|------|------|
| `TxId` | `string` | 交易 hash |
| `FromAddress` | `string` | 發送者地址 |
| `ToAddress` | `string` | 接收者地址 |
| `Amount` | `decimal` | 原生幣金額 |
| `Status` | `TransactionStatus` | `Unconfirmed` / `Confirmed` / `Failed` |
| `BlockNumber` | `long` | 區塊編號 |
| `Nonce` | `long` | 交易 nonce |
| `GasUsed` | `long` | 消耗 gas 量 |
| `GasPrice` | `BigInteger` | Effective gas price（Wei） |
| `Fee` | `decimal` | 手續費（ETH/POL） |
| `Failure` | `FailureInfo?` | 失敗原因（status = Failed 時） |

**狀態判斷：** 無 receipt = `Unconfirmed`，receipt status `0x1` = `Confirmed`，`0x0` = `Failed`

### GetBlockNumberAsync — 查區塊高度

```csharp
var result = await client.GetBlockNumberAsync();
Console.WriteLine($"Latest block: {result.Data}");
```

### GetErc20Contract — 建立 ERC20 合約實例

```csharp
using var contract = client.GetErc20Contract("0xContractAddress...");
```

回傳的 `Erc20Contract` 共用 `client.TokenCache`。

---

## ERC20 合約（Erc20Contract）

### 唯讀查詢（eth_call）

```csharp
using var contract = client.GetErc20Contract("0xdAC17F958D2ee523a2206206994597C13D831ec7");

// 取得完整 token 資訊（四查詢並行）
var info = await contract.GetTokenInfoAsync();
Console.WriteLine($"Name: {info.Data!.Name}");
Console.WriteLine($"Symbol: {info.Data.Symbol}");
Console.WriteLine($"Decimals: {info.Data.Decimals}");
Console.WriteLine($"TotalSupply: {info.Data.TotalSupply}");

// 個別查詢
var name = await contract.NameAsync();
var symbol = await contract.SymbolAsync();
var decimals = await contract.DecimalsAsync();   // 結果自動快取
var totalSupply = await contract.TotalSupplyAsync();

// 查餘額（回傳 raw amount）
var balance = await contract.BalanceOfAsync("0xHolderAddress...");
Console.WriteLine($"Raw balance: {balance.Data}"); // BigInteger

// 查授權額度
var allowance = await contract.AllowanceAsync("0xOwner...", "0xSpender...");
```

### 寫入操作（需簽名 + 廣播）

```csharp
// Transfer（金額為 raw amount，需自行乘以 decimals）
var tx = await contract.TransferAsync(account, "0xReceiverAddress...", rawAmount: 1_000_000);

// Approve（授權 spender 使用你的 token）
var approve = await contract.ApproveAsync(account, "0xSpenderAddress...", rawAmount: 1_000_000);
```

**寫入流程：** ABI 編碼 → estimateGas → getEip1559Fees → getNonce → buildTx → sign → sendRawTransaction

### TokenInfo 欄位

| 欄位 | 型別 | 說明 |
|------|------|------|
| `ContractAddress` | `string` | 合約地址 |
| `Name` | `string` | Token 名稱 |
| `Symbol` | `string` | Token 代號 |
| `Decimals` | `int` | 小數位數 |
| `TotalSupply` | `BigInteger` | 總供應量（raw） |
| `OriginAddress` | `string?` | 部署者地址 |

---

## 交易監聽（EvmTransactionWatcher）

### 基本使用

```csharp
using ChainKit.Evm.Watching;

// 選擇 block stream：Polling（HTTP 輪詢）或 WebSocket（即時推送）
var stream = new PollingBlockStream(provider, pollInterval: TimeSpan.FromSeconds(3));

// 建立 Watcher
await using var watcher = new EvmTransactionWatcher(
    stream, provider, EvmNetwork.Sepolia,
    confirmationBlocks: 12);      // 等待 12 個區塊確認

// 註冊事件
watcher.OnNativeReceived += (_, e) =>
    Console.WriteLine($"收到 {e.Amount} ETH from {e.FromAddress}");

watcher.OnErc20Received += (_, e) =>
    Console.WriteLine($"收到 {e.RawAmount} {e.Symbol} from {e.FromAddress}");

watcher.OnTransactionConfirmed += (_, e) =>
    Console.WriteLine($"交易確認：{e.TxId} at block {e.BlockNumber}");

watcher.OnTransactionFailed += (_, e) =>
    Console.WriteLine($"交易失敗：{e.TxId} - {e.Reason}");

// 監聽地址
watcher.WatchAddress("0xYourAddress...");
watcher.WatchAddresses(new[] { "0xAddr1...", "0xAddr2..." });

// 開始（從指定區塊或當前區塊開始）
var currentBlock = await provider.GetBlockNumberAsync();
await watcher.StartAsync(startBlock: currentBlock);
```

### 六事件

| 事件 | 參數型別 | 觸發時機 |
|------|----------|----------|
| `OnNativeReceived` | `NativeReceivedEventArgs` | 監聽地址收到原生幣 |
| `OnNativeSent` | `NativeSentEventArgs` | 監聽地址發出原生幣 |
| `OnErc20Received` | `Erc20ReceivedEventArgs` | 監聽地址收到 ERC20 token |
| `OnErc20Sent` | `Erc20SentEventArgs` | 監聯地址發出 ERC20 token |
| `OnTransactionConfirmed` | `TransactionConfirmedEventArgs` | 交易確認（receipt status 0x1 + N 個區塊深度） |
| `OnTransactionFailed` | `TransactionFailedEventArgs` | 交易失敗（receipt status 0x0）或逾時 |

### 事件參數

**NativeReceivedEventArgs / NativeSentEventArgs：**
| 欄位 | 型別 | 說明 |
|------|------|------|
| `TxId` | `string` | 交易 hash |
| `FromAddress` | `string` | 發送者 |
| `ToAddress` | `string` | 接收者 |
| `Amount` | `decimal` | 金額（ETH/POL） |
| `RawAmount` | `BigInteger` | 金額（Wei） |

**Erc20ReceivedEventArgs / Erc20SentEventArgs：**
| 欄位 | 型別 | 說明 |
|------|------|------|
| `TxId` | `string` | 交易 hash |
| `ContractAddress` | `string` | ERC20 合約地址 |
| `FromAddress` | `string` | 發送者 |
| `ToAddress` | `string` | 接收者 |
| `RawAmount` | `BigInteger` | Token raw amount |
| `Amount` | `decimal?` | Token 金額（有 decimals 時） |
| `Symbol` | `string?` | Token 代號（有快取時） |

### Block Stream 選擇

**PollingBlockStream（推薦）：**
```csharp
var stream = new PollingBlockStream(provider, pollInterval: TimeSpan.FromSeconds(3));
```
- 用 `eth_getBlockByNumber` 定期輪詢
- 所有 RPC 節點都支援
- 適合大多數場景

**WebSocketBlockStream：**
```csharp
var stream = new WebSocketBlockStream("wss://your-node.com/ws", provider);
```
- 用 `eth_subscribe("newHeads")` 即時推送
- 延遲更低
- 自動重連（指數退避：1s → 2s → 4s → ... → 30s）
- 斷線後自動補漏（從最後處理的 block 繼續）
- 需要節點支援 WebSocket

### 確認機制

| 參數 | 預設值 | 說明 |
|------|--------|------|
| `confirmationBlocks` | 12 | 等待幾個區塊深度後觸發 Confirmed |
| `confirmationIntervalMs` | 5000 | 確認輪詢間隔（毫秒） |
| `maxPendingAge` | 30 分鐘 | 超過此時間未確認的交易觸發 Failed |

### ERC20 偵測方式

使用 receipt logs 解析 `Transfer(address,address,uint256)` event：
- `topics[0]` = `0xddf252ad...`（Transfer event signature）
- `topics[1]` = from 地址
- `topics[2]` = to 地址
- `data` = amount

這比 Tron 的 input data 解析方式更可靠，因為 logs 反映實際執行結果（包含合約內部 transfer）。

### 生命週期

```csharp
await watcher.StartAsync(startBlock: 12345);  // 開始監聽
// ... 執行中 ...
await watcher.StopAsync();                     // 停止監聽
await watcher.DisposeAsync();                  // 釋放資源
```

---

## 錯誤處理

### Result Pattern

所有高階 API 回傳 `EvmResult<T>`（繼承 `ChainResult<T>`），業務錯誤不 throw exception：

```csharp
var result = await client.TransferAsync(account, "0x...", 1.0m);

if (result.Success)
{
    Console.WriteLine($"TxHash: {result.Data!.TxId}");
}
else
{
    Console.WriteLine($"Error Code: {result.ErrorCode}");
    Console.WriteLine($"Message: {result.Error!.Message}");
    Console.WriteLine($"Raw: {result.Error.RawMessage}");
}
```

### EvmErrorCode 列表

| 錯誤碼 | 說明 |
|--------|------|
| `Unknown` | 未知錯誤 |
| `InvalidAddress` | 地址格式不正確 |
| `InvalidAmount` | 金額不正確（≤0 或 overflow） |
| `InsufficientBalance` | 餘額不足 |
| `InsufficientGasBalance` | Gas 費用不足 |
| `NonceTooLow` | Nonce 已被使用 |
| `NonceTooHigh` | Nonce 超前太多 |
| `GasPriceTooLow` | Gas 價格太低 |
| `GasLimitExceeded` | 超出 gas 上限 |
| `ContractReverted` | 合約執行 revert |
| `ContractNotFound` | 合約不存在 |
| `TransactionNotFound` | 交易不存在 |
| `ProviderConnectionFailed` | RPC 連線失敗 |
| `ProviderTimeout` | RPC 逾時 |
| `ProviderRpcError` | RPC 回傳錯誤 |

---

## 金額轉換

### 原生幣（ETH/POL）

高階 API（`EvmClient`）使用 `decimal` ETH/POL，低階自動轉換為 `BigInteger` Wei。

```
1 ETH = 1,000,000,000,000,000,000 Wei (10^18)
```

```csharp
using ChainKit.Core.Converters;

// ETH → Wei
BigInteger wei = TokenConverter.ToRawAmount(1.5m, 18);  // 1500000000000000000

// Wei → ETH
decimal eth = TokenConverter.ToTokenAmount(wei, 18);     // 1.5
```

### ERC20 Token

`Erc20Contract` 的 `BalanceOfAsync` 和 `TransferAsync` 使用 raw amount（最小單位）：

```csharp
// USDT (6 decimals): 1 USDT = 1,000,000 raw units
BigInteger rawAmount = TokenConverter.ToRawAmount(100m, 6);  // 100_000_000
await contract.TransferAsync(account, "0x...", rawAmount);

// 反向：raw → 人類可讀
decimal amount = TokenConverter.ToTokenAmount(rawAmount, 6);  // 100.0
```

注意：`TokenConverter.DecimalPow10` 使用 decimal 迴圈乘法，避免 `Math.Pow` 的 double 精度損失。

---

## 低階 API

### IEvmProvider

所有 JSON-RPC 方法的介面，`EvmHttpProvider` 是 HTTP 實作：

| 方法 | JSON-RPC | 說明 |
|------|----------|------|
| `GetBalanceAsync` | `eth_getBalance` | 查餘額（Wei） |
| `GetTransactionCountAsync` | `eth_getTransactionCount` | 查 nonce |
| `GetCodeAsync` | `eth_getCode` | 查合約 bytecode |
| `GetBlockByNumberAsync` | `eth_getBlockByNumber` | 查區塊 |
| `GetBlockNumberAsync` | `eth_blockNumber` | 最新區塊高度 |
| `SendRawTransactionAsync` | `eth_sendRawTransaction` | 廣播交易 |
| `GetTransactionByHashAsync` | `eth_getTransactionByHash` | 查交易 |
| `GetTransactionReceiptAsync` | `eth_getTransactionReceipt` | 查交易收據 |
| `CallAsync` | `eth_call` | 唯讀合約呼叫 |
| `EstimateGasAsync` | `eth_estimateGas` | 估算 gas |
| `GetGasPriceAsync` | `eth_gasPrice` | 查 gas 價格（Legacy） |
| `GetEip1559FeesAsync` | `eth_getBlockByNumber` + `eth_maxPriorityFeePerGas` | 查 EIP-1559 費用 |
| `GetLogsAsync` | `eth_getLogs` | 查事件 logs |

### 交易簽名工具

```csharp
using ChainKit.Evm.Crypto;
using ChainKit.Evm.Protocol;

// EIP-1559 簽名
var sig = EvmSigner.SignTyped(txHash, privateKey);      // v = recoveryId (0 or 1)

// Legacy (EIP-155) 簽名
var sig = EvmSigner.SignLegacy(txHash, privateKey, chainId: 1);  // v = chainId*2+35+recoveryId

// 完整交易流程
var (txHash, rawTx) = EvmTransactionUtils.SignEip1559Transaction(
    chainId: 1, nonce: 0,
    maxPriorityFeePerGas: 1000, maxFeePerGas: 2000, gasLimit: 21000,
    to: "0x...", value: BigInteger.Zero, data: Array.Empty<byte>(),
    privateKey: account.PrivateKey);
```

### RLP 編碼

```csharp
using ChainKit.Evm.Protocol;

var encoded = RlpEncoder.EncodeElement(new byte[] { 0x04, 0x00 });  // integer 1024
var list = RlpEncoder.EncodeList(item1, item2, item3);               // RLP list
var uint256 = RlpEncoder.EncodeUint(new BigInteger(1000));           // unsigned integer
var longVal = RlpEncoder.EncodeLong(21000);                          // long integer
```

---

## IDisposable / IAsyncDisposable

| 類別 | 介面 | 說明 |
|------|------|------|
| `EvmClient` | `IDisposable` | 不 dispose Provider（外部擁有） |
| `EvmHttpProvider` | `IDisposable` | 釋放 HttpClient |
| `Erc20Contract` | `IDisposable` | 釋放 SemaphoreSlim |
| `EvmAccount` | `IDisposable` | 清零 PrivateKey |
| `EvmTransactionWatcher` | `IAsyncDisposable` | 停止監聽、釋放資源 |

建議搭配 `using` 使用：

```csharp
using var provider = new EvmHttpProvider(EvmNetwork.Sepolia);
using var client = new EvmClient(provider, EvmNetwork.Sepolia);
using var account = EvmAccount.FromPrivateKey(key);
using var contract = client.GetErc20Contract("0x...");
await using var watcher = new EvmTransactionWatcher(stream, provider, network);
```
