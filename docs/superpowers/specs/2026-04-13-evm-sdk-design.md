# ChainKit.Evm SDK 設計規格

## 概述

為 ChainKit 新增 EVM 相容鏈支援（Ethereum + Polygon），以 `ChainKit.Evm` 單一專案涵蓋所有 EVM 鏈。同時重構 `ChainKit.Core`，將 Tron 和 EVM 共用的 crypto 元件提升到 Core 層。

## 設計決策摘要

| 決策 | 選擇 | 原因 |
|------|------|------|
| 專案命名 | `ChainKit.Evm`（非 `ChainKit.Ethereum`） | 一套程式碼支援所有 EVM 鏈，用 network config 區分 |
| 合約標準 | 僅 ERC20 | 與 Tron SDK 的 TRC20 對齊 |
| 外部依賴 | 不使用 Nethereum | ChainKit 已有 Keccak256/ABI/Secp256k1，只缺 RLP（自己寫） |
| 共用元件 | Keccak256/AbiEncoder(通用)/Mnemonic/TokenConverter 搬到 Core | 消除重複，原始碼分析確認這些無鏈特定邏輯 |
| Watcher | Polling + WebSocket 兩種實作 | 使用者建構時選擇，同 Tron 的 PollingBlockStream/ZmqBlockStream 模式 |
| 測試策略 | Unit (mock) + Integration (Anvil) + E2E (Sepolia) | Anvil 提供免費可控的本地 EVM 節點 |

---

## 1. Core 重構

### 新增至 `ChainKit.Core`

```
src/ChainKit.Core/
├── Crypto/
│   ├── Keccak256.cs          ← 從 Tron/Crypto/ 搬過來，改 namespace
│   ├── Mnemonic.cs           ← 從 Tron/Crypto/ 搬過來，改 namespace
│   └── AbiEncoder.cs         ← 從 Tron 拆出通用部分
├── Converters/
│   └── TokenConverter.cs     ← 從 TronConverter 拆出通用部分
```

### Keccak256

原封不動搬到 `ChainKit.Core.Crypto` namespace。純演算法，零鏈特定邏輯。

### AbiEncoder（Core 版）

保留通用方法，移除地址相關方法：

```csharp
namespace ChainKit.Core.Crypto;

public static class AbiEncoder
{
    public static byte[] EncodeFunctionSelector(string signature);  // Keccak256(sig)[..4]
    public static byte[] EncodeUint256(BigInteger value);           // 32-byte big-endian
    public static byte[] EncodeBytes32(byte[] data);                // 32-byte padded
    public static BigInteger DecodeUint256(byte[] data);
    public static string DecodeString(byte[] data);
}
```

`EncodeAddress`/`DecodeAddress` 不在 Core — 各鏈地址格式不同：
- Tron：`41` 前綴 hex（現有 `TronAbiEncoder`）
- EVM：20-byte raw hex（新的 `EvmAbiEncoder`）

### Mnemonic

原封不動搬到 `ChainKit.Core.Crypto` namespace。BIP39 標準，無鏈特定邏輯。BIP44 路徑（`m/44'/195'` vs `m/44'/60'`）由各鏈的 Account 層處理。

### TokenConverter（Core 版）

```csharp
namespace ChainKit.Core.Converters;

public static class TokenConverter
{
    // decimal 迴圈乘法（禁用 Math.Pow，避免 double 精度損失）
    public static decimal DecimalPow10(int exponent);
    
    // RawAmount → 人類可讀（除以 10^decimals）
    public static decimal? ToTokenAmount(BigInteger rawAmount, int? decimals);
    
    // 人類可讀 → RawAmount（乘以 10^decimals）
    public static BigInteger ToRawAmount(decimal amount, int decimals);
}
```

### Tron 對應調整

| 檔案 | 變更 |
|------|------|
| `Tron/Crypto/Keccak256.cs` | 刪除，改 using `ChainKit.Core.Crypto` |
| `Tron/Crypto/Mnemonic.cs` | 刪除，改 using `ChainKit.Core.Crypto` |
| `Tron/Crypto/AbiEncoder.cs` | 改名 `TronAbiEncoder`，只保留 `EncodeAddress`/`DecodeAddress`（41 前綴），其餘方法委託給 `Core.Crypto.AbiEncoder` |
| `Tron/Crypto/TronConverter.cs` | 只保留 `SunToTrx`/`TrxToSun`，通用方法改呼叫 `Core.Converters.TokenConverter` |
| `Tron/Crypto/TronSigner.cs` | 不變 |
| `Tron/Crypto/TronAddress.cs` | 不變 |
| `Tron/Crypto/TronAccount.cs` | 改 using 指向 Core 的 Keccak256/Mnemonic |

---

## 2. 專案結構

```
src/ChainKit.Evm/
├── Crypto/
│   ├── EvmAccount.cs
│   ├── EvmSigner.cs
│   ├── EvmAddress.cs
│   └── EvmAbiEncoder.cs
├── Protocol/
│   ├── RlpEncoder.cs
│   ├── TransactionBuilder.cs
│   └── TransactionUtils.cs
├── Providers/
│   ├── IEvmProvider.cs
│   ├── EvmHttpProvider.cs
│   └── EvmNetwork.cs
├── Contracts/
│   ├── Erc20Contract.cs
│   └── TokenInfoCache.cs
├── Watching/
│   ├── IEvmBlockStream.cs
│   ├── PollingBlockStream.cs
│   ├── WebSocketBlockStream.cs
│   └── EvmTransactionWatcher.cs
├── Models/
│   ├── EvmResult.cs
│   ├── EvmErrorCode.cs
│   ├── TransactionModels.cs
│   ├── AccountModels.cs
│   └── WatcherModels.cs
└── EvmClient.cs
```

Solution 新增：

```
ChainKit.slnx
├── src/ChainKit.Evm
├── tests/ChainKit.Evm.Tests
└── sandbox/ChainKit.Sandbox（擴展 EVM endpoints）
```

NuGet 依賴：
- `NBitcoin.Secp256k1` — ECDSA 簽名（同 Tron）
- `NBitcoin` — BIP44 HD derivation（同 Tron，透過 Core 的 Mnemonic）
- `Microsoft.Extensions.Logging.Abstractions` — optional ILogger（同 Tron）
- 無 Nethereum 依賴

---

## 3. Crypto 層

### EvmAddress

```csharp
public static class EvmAddress
{
    public static bool IsValid(string address);
    public static string ToChecksumAddress(string address);  // EIP-55
    public static string FromPublicKey(byte[] uncompressedPublicKey);
    // Keccak256(pubkey[1..]) 取最後 20 bytes → 0x hex → EIP-55 checksum
}
```

### EvmAccount

```csharp
public sealed class EvmAccount : IAccount, IDisposable
{
    public string Address { get; }         // 0x checksum 格式
    public byte[] PublicKey { get; }       // 33 bytes compressed
    internal byte[] PrivateKey { get; }
    
    public static EvmAccount Create();
    public static EvmAccount FromPrivateKey(byte[] privateKey);
    public static EvmAccount FromMnemonic(string mnemonic, int index = 0);
    // BIP44 路徑：m/44'/60'/0'/0/{index}
    
    public void Dispose();  // 清零 PrivateKey
}
```

### EvmSigner

```csharp
public static class EvmSigner
{
    // Legacy（EIP-155）：v = chainId * 2 + 35 + recoveryId
    public static byte[] SignLegacy(byte[] txHash, byte[] privateKey, long chainId);
    
    // EIP-1559（Type 2）：v = recoveryId（0 or 1）
    public static byte[] SignTyped(byte[] txHash, byte[] privateKey);
    
    public static bool Verify(byte[] data, byte[] signature, byte[] publicKey);
}
```

底層都用 `NBitcoin.Secp256k1` 的 `ECPrivKey.TrySignRecoverable`。

### EvmAbiEncoder

```csharp
public static class EvmAbiEncoder
{
    // 地址編碼：去掉 0x 前綴，20 bytes 左 pad 到 32 bytes
    public static byte[] EncodeAddress(string address);
    
    // 地址解碼：取最後 20 bytes → 0x hex
    public static string DecodeAddress(byte[] data);
    
    // 便利方法（組合 Core.AbiEncoder + 地址）
    public static byte[] EncodeTransfer(string toAddress, BigInteger amount);
    public static byte[] EncodeBalanceOf(string address);
    public static byte[] EncodeApprove(string spender, BigInteger amount);
    public static byte[] EncodeAllowance(string owner, string spender);
}
```

---

## 4. Protocol 層

### RlpEncoder

自己寫，約 100-150 行。規則：

| 資料 | 編碼 |
|------|------|
| 單 byte 0x00-0x7f | 直接輸出 |
| 0-55 bytes | `(0x80 + length)` + data |
| >55 bytes | `(0xb7 + lengthOfLength)` + length + data |
| List（0-55 bytes total） | `(0xc0 + length)` + items |
| List（>55 bytes total） | `(0xf7 + lengthOfLength)` + length + items |

```csharp
public static class RlpEncoder
{
    public static byte[] EncodeElement(byte[] data);
    public static byte[] EncodeList(params byte[][] items);
    public static RlpItem Decode(byte[] data);
}
```

### TransactionBuilder

支援兩種交易格式：

```csharp
public static class EvmTransactionBuilder
{
    // EIP-1559（Type 2，預設）
    // 編碼：0x02 || RLP([chainId, nonce, maxPriorityFee, maxFee, gasLimit, to, value, data, accessList, v, r, s])
    public static byte[] BuildEip1559(
        long chainId, long nonce,
        long maxPriorityFeePerGas, long maxFeePerGas, long gasLimit,
        string to, BigInteger value, byte[] data,
        byte[]? signature);

    // Legacy（EIP-155）
    // 簽名 hash：RLP([nonce, gasPrice, gasLimit, to, value, data, chainId, 0, 0])
    // 編碼：RLP([nonce, gasPrice, gasLimit, to, value, data, v, r, s])
    public static byte[] BuildLegacy(
        long nonce, long gasPrice, long gasLimit,
        string to, BigInteger value, byte[] data,
        long chainId, byte[]? signature);
}
```

### TransactionUtils

```csharp
public static class EvmTransactionUtils
{
    public static byte[] ComputeSigningHash(byte[] unsignedTx);  // Keccak256
    public static byte[] ComputeTxHash(byte[] signedTx);          // Keccak256
    
    public static (string txHash, byte[] rawTx) SignTransaction(
        EvmTransactionBuilder.Eip1559Params txParams, byte[] privateKey);
}
```

### 交易流程

1. `GetTransactionCount` → nonce
2. `EstimateGas` → gasLimit
3. `GetEip1559Fees` → maxFeePerGas + maxPriorityFeePerGas
4. `BuildEip1559(signature: null)` → 未簽名交易
5. `ComputeSigningHash` → Keccak256
6. `EvmSigner.SignTyped` → 簽名
7. `BuildEip1559(signature: sig)` → 已簽名交易
8. `SendRawTransaction` → 廣播

---

## 5. Provider 層

### IEvmProvider

```csharp
public interface IEvmProvider : IDisposable
{
    // 帳戶
    Task<BigInteger> GetBalanceAsync(string address, CancellationToken ct = default);
    Task<long> GetTransactionCountAsync(string address, CancellationToken ct = default);
    Task<string> GetCodeAsync(string address, CancellationToken ct = default);

    // 區塊
    Task<JsonElement?> GetBlockByNumberAsync(long blockNumber, bool fullTx = false, CancellationToken ct = default);
    Task<long> GetBlockNumberAsync(CancellationToken ct = default);

    // 交易
    Task<string> SendRawTransactionAsync(byte[] signedTx, CancellationToken ct = default);
    Task<JsonElement?> GetTransactionByHashAsync(string txHash, CancellationToken ct = default);
    Task<JsonElement?> GetTransactionReceiptAsync(string txHash, CancellationToken ct = default);

    // 合約
    Task<string> CallAsync(string to, byte[] data, CancellationToken ct = default);
    Task<long> EstimateGasAsync(string from, string to, byte[] data, BigInteger? value = null, CancellationToken ct = default);

    // Gas
    Task<BigInteger> GetGasPriceAsync(CancellationToken ct = default);
    Task<(BigInteger baseFee, BigInteger priorityFee)> GetEip1559FeesAsync(CancellationToken ct = default);

    // Logs
    Task<JsonElement[]> GetLogsAsync(long fromBlock, long toBlock, string? address = null, string[]? topics = null, CancellationToken ct = default);
}
```

### EvmHttpProvider

所有方法底層走 JSON-RPC 2.0：`POST { "jsonrpc": "2.0", "method": "eth_xxx", "params": [...], "id": N }`

與 `TronHttpProvider` 的差異：
- Tron 用 REST-like HTTP API（每個方法一個 URL path），EVM 用 JSON-RPC 2.0（單一 URL）
- Tron 需要雙端點（Full Node + Solidity Node），EVM 只需要一個端點
- 序列化：Tron 用 CamelCase，EVM 的 JSON-RPC 回應欄位名稱由節點決定（通常 camelCase）

```csharp
public sealed class EvmHttpProvider : IEvmProvider
{
    public EvmHttpProvider(string rpcUrl, ILogger<EvmHttpProvider>? logger = null);
    public EvmHttpProvider(EvmNetworkConfig network, ILogger<EvmHttpProvider>? logger = null);
}
```

### EvmNetwork

```csharp
public record EvmNetworkConfig(string RpcUrl, long ChainId, string Name, string NativeCurrency, int Decimals = 18);

public static class EvmNetwork
{
    public static readonly EvmNetworkConfig EthereumMainnet = new("https://eth.llamarpc.com", 1, "Ethereum Mainnet", "ETH");
    public static readonly EvmNetworkConfig Sepolia = new("https://rpc.sepolia.org", 11155111, "Sepolia", "ETH");
    public static readonly EvmNetworkConfig PolygonMainnet = new("https://polygon-rpc.com", 137, "Polygon", "POL");
    public static readonly EvmNetworkConfig PolygonAmoy = new("https://rpc-amoy.polygon.technology", 80002, "Polygon Amoy", "POL");
    
    public static EvmNetworkConfig Custom(string rpcUrl, long chainId, string name, string nativeCurrency)
        => new(rpcUrl, chainId, name, nativeCurrency);
}
```

---

## 6. Models 層

### EvmErrorCode

```csharp
public enum EvmErrorCode
{
    Unknown,
    InvalidAddress,
    InvalidAmount,
    InsufficientBalance,
    InsufficientGasBalance,
    NonceTooLow,
    NonceTooHigh,
    GasPriceTooLow,
    GasLimitExceeded,
    ContractReverted,
    ContractNotFound,
    TransactionNotFound,
    ProviderConnectionFailed,
    ProviderTimeout,
    ProviderRpcError
}
```

### EvmResult

```csharp
public record EvmResult<T> : ChainResult<T>
{
    public EvmErrorCode? ErrorCode { get; init; }
    
    public static EvmResult<T> Ok(T data) => new() { Success = true, Data = data };
    public static EvmResult<T> Fail(EvmErrorCode code, string message, string? rawMessage = null)
        => new() { Success = false, ErrorCode = code, Error = new ChainError(code.ToString(), message, rawMessage) };
}
```

### Transaction Models

```csharp
public record TransferResult(string TxId);

public enum TransactionStatus { Unconfirmed, Confirmed, Failed }

public enum TransactionType { NativeTransfer, ContractCall, ContractCreation, Erc20Transfer }

public record EvmTransactionDetail : ITransaction
{
    public string TxId { get; init; }
    public string FromAddress { get; init; }
    public string ToAddress { get; init; }
    public decimal Amount { get; init; }          // ETH/POL
    public DateTime Timestamp { get; init; }
    public TransactionStatus Status { get; init; }
    public long BlockNumber { get; init; }
    public long Nonce { get; init; }
    public long GasUsed { get; init; }
    public BigInteger GasPrice { get; init; }     // Wei
    public decimal Fee { get; init; }             // ETH/POL
    public TokenTransferInfo? TokenTransfer { get; init; }
    public FailureInfo? Failure { get; init; }
}

public record TokenTransferInfo(
    string ContractAddress, string FromAddress, string ToAddress,
    BigInteger RawAmount, decimal? Amount, string? Symbol);

public record FailureInfo(string Reason, string? RevertData);
```

### Account Models

```csharp
public record BalanceInfo(decimal Balance, BigInteger RawBalance);

public record TokenBalanceInfo(
    string ContractAddress, BigInteger RawBalance,
    decimal? Balance, string? Symbol, int? Decimals);

public record TokenInfo(
    string ContractAddress, string Name, string Symbol,
    int Decimals, BigInteger TotalSupply, string? OriginAddress);

public record BlockInfo(long BlockNumber, string BlockHash, DateTime Timestamp, int TransactionCount);
```

### Watcher Models

```csharp
public record EvmBlock(long BlockNumber, string BlockHash, DateTime Timestamp, List<EvmBlockTransaction> Transactions);
public record EvmBlockTransaction(string TxHash, string From, string To, BigInteger Value, byte[] Input, JsonElement? Receipt);

// 六事件
public record NativeReceivedEventArgs(string TxId, string FromAddress, string ToAddress, decimal Amount, BigInteger RawAmount);
public record NativeSentEventArgs(string TxId, string FromAddress, string ToAddress, decimal Amount, BigInteger RawAmount);
public record Erc20ReceivedEventArgs(string TxId, string ContractAddress, string FromAddress, string ToAddress, BigInteger RawAmount, decimal? Amount, string? Symbol);
public record Erc20SentEventArgs(string TxId, string ContractAddress, string FromAddress, string ToAddress, BigInteger RawAmount, decimal? Amount, string? Symbol);
public record TransactionConfirmedEventArgs(string TxId, long BlockNumber);
public record TransactionFailedEventArgs(string TxId, string Reason);
```

---

## 7. Contracts 層

### Erc20Contract

```csharp
public sealed class Erc20Contract : IDisposable
{
    public Erc20Contract(IEvmProvider provider, string contractAddress,
        EvmNetworkConfig network, TokenInfoCache? tokenCache = null,
        ILogger<Erc20Contract>? logger = null);

    // 唯讀（eth_call）
    public Task<EvmResult<TokenInfo>> GetTokenInfoAsync(CancellationToken ct = default);
    public Task<EvmResult<string>> NameAsync(CancellationToken ct = default);
    public Task<EvmResult<string>> SymbolAsync(CancellationToken ct = default);
    public Task<EvmResult<int>> DecimalsAsync(CancellationToken ct = default);
    public Task<EvmResult<BigInteger>> TotalSupplyAsync(CancellationToken ct = default);
    public Task<EvmResult<BigInteger>> BalanceOfAsync(string address, CancellationToken ct = default);
    public Task<EvmResult<BigInteger>> AllowanceAsync(string owner, string spender, CancellationToken ct = default);

    // 寫入（簽名 + 廣播）
    public Task<EvmResult<TransferResult>> TransferAsync(EvmAccount from, string toAddress, BigInteger rawAmount, CancellationToken ct = default);
    public Task<EvmResult<TransferResult>> ApproveAsync(EvmAccount from, string spenderAddress, BigInteger rawAmount, CancellationToken ct = default);
}
```

寫入操作流程：ABI 編碼 → estimateGas → getEip1559Fees → getNonce → buildTx → sign → sendRawTransaction

ERC20 Transfer 偵測（Watcher 用）：解析 receipt logs 的 `Transfer(address,address,uint256)` event topic（`0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef`），比解析 input data 更可靠。

### TokenInfoCache

三層快取（同 Tron 模式）：
1. 內建表（按 chainId 分組的常見 token）
2. Memory cache（`ConcurrentDictionary`，key = `chainId:address`）
3. 合約呼叫（cache miss 時）

```csharp
public class TokenInfoCache
{
    public TokenInfoCache(ILogger? logger = null);
    
    public Task<TokenInfo?> GetOrResolveAsync(
        string contractAddress, long chainId,
        Func<string, Task<TokenInfo?>> resolveFromContract,
        CancellationToken ct = default);
}
```

---

## 8. Watching 層

### IEvmBlockStream

```csharp
public interface IEvmBlockStream : IAsyncDisposable
{
    IAsyncEnumerable<EvmBlock> GetBlocksAsync(long startBlock, CancellationToken ct = default);
}
```

### PollingBlockStream

用 `eth_getBlockByNumber` 定期輪詢，逐塊遞增。

```csharp
public sealed class PollingBlockStream : IEvmBlockStream
{
    public PollingBlockStream(IEvmProvider provider, TimeSpan? pollInterval = null,
        ILogger<PollingBlockStream>? logger = null);
    // pollInterval 預設 3 秒
}
```

### WebSocketBlockStream

用 `eth_subscribe("newHeads")` 接收新區塊通知，收到後用 provider 的 `GetBlockByNumber(full=true)` 拿完整區塊。

```csharp
public sealed class WebSocketBlockStream : IEvmBlockStream
{
    public WebSocketBlockStream(string wsUrl, IEvmProvider provider,
        ILogger<WebSocketBlockStream>? logger = null);
}
```

功能：
- 斷線自動重連（指數退避）
- 補漏：記住最後處理的 blockNumber，重連後從該塊繼續輪詢補上

### EvmTransactionWatcher

```csharp
public sealed class EvmTransactionWatcher : IAsyncDisposable
{
    public EvmTransactionWatcher(IEvmBlockStream blockStream, IEvmProvider provider,
        EvmNetworkConfig network, TokenInfoCache? tokenCache = null,
        ILogger<EvmTransactionWatcher>? logger = null);

    // 地址管理（HashSet，同 Tron）
    public void WatchAddress(string address);
    public void UnwatchAddress(string address);

    // 六事件
    public event EventHandler<NativeReceivedEventArgs>? OnNativeReceived;
    public event EventHandler<NativeSentEventArgs>? OnNativeSent;
    public event EventHandler<Erc20ReceivedEventArgs>? OnErc20Received;
    public event EventHandler<Erc20SentEventArgs>? OnErc20Sent;
    public event EventHandler<TransactionConfirmedEventArgs>? OnTransactionConfirmed;
    public event EventHandler<TransactionFailedEventArgs>? OnTransactionFailed;

    // 三階段生命週期（同 Tron）
    public Task StartAsync(long? startBlock = null, CancellationToken ct = default);
    public Task StopAsync();
    public ValueTask DisposeAsync();
}
```

交易確認機制（與 Tron 不同）：
- **Tron**：查 Solidity Node，空物件 `{}` = 未確認，`receipt.result == SUCCESS` = 確認
- **EVM**：查 `eth_getTransactionReceipt`，receipt 存在且 `status == 0x1` = 確認，`status == 0x0` = 失敗。額外等待 N 個 block confirmations（可設定，預設 12）

---

## 9. EvmClient Facade

```csharp
public sealed class EvmClient : IDisposable
{
    public IEvmProvider Provider { get; }
    public TokenInfoCache TokenCache { get; }
    public EvmNetworkConfig Network { get; }
    
    public EvmClient(IEvmProvider provider, EvmNetworkConfig network,
        ILogger<EvmClient>? logger = null);

    // 原生幣
    public Task<EvmResult<TransferResult>> TransferAsync(
        EvmAccount from, string toAddress, decimal amount, CancellationToken ct = default);
    public Task<EvmResult<BalanceInfo>> GetBalanceAsync(string address, CancellationToken ct = default);

    // 交易查詢
    public Task<EvmResult<EvmTransactionDetail>> GetTransactionDetailAsync(
        string txHash, CancellationToken ct = default);

    // ERC20
    public Erc20Contract GetErc20Contract(string contractAddress);

    // 合約部署
    public Task<EvmResult<TransferResult>> DeployContractAsync(
        EvmAccount from, byte[] bytecode, CancellationToken ct = default);

    // 工具
    public Task<EvmResult<long>> GetBlockNumberAsync(CancellationToken ct = default);

    public void Dispose();
}
```

ERC20 操作統一走 `Erc20Contract`（同 Tron 的 ADR 011：`TronClient` 不包 TRC20 transfer）。

---

## 10. 測試策略

### 測試層級

| 層級 | 工具 | 涵蓋範圍 |
|------|------|----------|
| Unit Tests | NSubstitute mock `IEvmProvider` | 業務邏輯、RLP 編碼、簽名、地址驗證、ABI 編碼、Watcher 事件 |
| Integration Tests | Anvil 本地 EVM 節點 | 真實交易流程、ERC20 操作、合約部署、Watcher 端到端 |
| E2E Tests（少量） | Sepolia 測試網 | 驗證真實網路行為與本地節點一致 |

### Anvil 使用方式

- 安裝：`curl -L https://foundry.paradigm.xyz | bash && foundryup`
- 啟動：`anvil`（預設 http://127.0.0.1:8545，10 個帳戶各 10,000 ETH）
- Fork 主網：`anvil --fork-url <mainnet-rpc>`
- CI：作為 test setup 自動啟動/關閉

### 測試分類

```csharp
[Trait("Category", "Unit")]       // dotnet test --filter "Category!=Integration"
[Trait("Category", "Integration")] // dotnet test --filter "Category=Integration" （需要 Anvil）
[Trait("Category", "E2E")]         // dotnet test --filter "Category=E2E" （需要 Sepolia）
```

### 測試涵蓋範圍

**Unit Tests：**
- RlpEncoder：各種 edge case（空值、單 byte、長資料、巢狀 list）
- EvmSigner：Legacy/EIP-1559 簽名、v 值正確性、已知向量驗證
- EvmAddress：格式驗證、EIP-55 checksum、公鑰推導
- EvmAbiEncoder：function selector、地址編解碼、uint256
- EvmClient：轉帳流程（mock provider）、錯誤處理、金額驗證
- Erc20Contract：token 查詢、transfer/approve（mock provider）
- EvmTransactionWatcher：事件觸發、地址過濾、確認/失敗判斷
- TokenInfoCache：三層快取命中順序、併發安全

**Integration Tests（Anvil）：**
- 完整轉帳流程（建 tx → 簽名 → 廣播 → 確認）
- ERC20 部署 + transfer + balanceOf
- Watcher 監聽即時交易
- 多帳戶併發操作
- Gas 估算準確性

**E2E Tests（Sepolia）：**
- 真實網路轉帳
- 查詢已知交易/區塊
- 驗證 gas 機制行為

---

## 11. Sandbox 擴展

在現有 `ChainKit.Sandbox` 新增 EVM endpoints，結構同 Tron endpoints：

| 功能 | Endpoint |
|------|----------|
| 查餘額 | `GET /evm/balance/{address}` |
| 轉帳 | `POST /evm/transfer` |
| 查交易 | `GET /evm/transaction/{txHash}` |
| ERC20 Token Info | `GET /evm/erc20/{contract}/info` |
| ERC20 餘額 | `GET /evm/erc20/{contract}/balance/{address}` |
| ERC20 Transfer | `POST /evm/erc20/{contract}/transfer` |
| 查區塊高度 | `GET /evm/block-number` |

所有 endpoint 接受 `network` query parameter（預設 Sepolia）：`?network=ethereum-mainnet`

---

## 12. 與 Tron SDK 的對照表

| 概念 | Tron SDK | EVM SDK |
|------|----------|---------|
| 帳戶 | `TronAccount` (Base58) | `EvmAccount` (0x checksum) |
| 簽名 | `TronSigner` (recovery_id) | `EvmSigner` (EIP-155/1559) |
| 地址 | `TronAddress` (41 prefix + Base58) | `EvmAddress` (EIP-55 checksum) |
| ABI | `TronAbiEncoder` (41 prefix) | `EvmAbiEncoder` (0x prefix) |
| 交易格式 | Protobuf | RLP |
| 交易序列化 | `TransactionBuilder` (Protobuf) | `EvmTransactionBuilder` (RLP) |
| Provider | `ITronProvider` (REST HTTP) | `IEvmProvider` (JSON-RPC 2.0) |
| 雙端點 | Full Node + Solidity Node | 單一 RPC 端點 |
| Token 合約 | `Trc20Contract` | `Erc20Contract` |
| Block Stream | `PollingBlockStream` + `ZmqBlockStream` | `PollingBlockStream` + `WebSocketBlockStream` |
| Watcher | `TronTransactionWatcher` | `EvmTransactionWatcher` |
| 交易確認 | Solidity Node 查詢 | receipt status + N confirmations |
| ERC20 偵測 | 解析 input data | 解析 receipt logs (Transfer event) |
| 原生幣單位 | TRX / Sun (1:1M) | ETH / Wei (1:10^18) |
| 資源模型 | Energy + Bandwidth | Gas |
| 網路設定 | `TronNetwork` (Mainnet/Nile/Shasta) | `EvmNetwork` (Ethereum/Sepolia/Polygon/Amoy) |
| Result | `TronResult<T>` | `EvmResult<T>` |
| Facade | `TronClient` | `EvmClient` |
