# ChainKit Tron SDK 設計文件

> **注意：** 此為實作前的設計規格，部分內容已與實作不同。主要差異：
> - `TransactionStatus.NotFound` 已移除（見 `docs/decisions/002-transaction-status-notfound-removal.md`）
> - Watcher 已增強為雙向監聽 + 三階段生命週期（見 `docs/superpowers/specs/2026-04-04-watcher-lifecycle-design.md`）

## 概述

ChainKit 是多鏈 SDK 解決方案，供內部系統使用。各系統根據需要的區塊鏈安裝對應套件。Tron 是第一條支援的鏈。

### 設計原則

- **單套件per鏈**：`ChainKit.Tron` 一個套件包含所有 Tron 功能，內部用 namespace 分層
- **高低階 API 並存**：高階一步到位，低階靈活控制
- **統一 Result Pattern**：業務錯誤不 throw exception，只有 SDK 內部 bug 才會 throw
- **Provider 抽象**：HTTP (TronGrid) / gRPC (Full Node) 可選

### 單位約定

- 高階 API（`TronClient`）：使用 `decimal`，單位為 **TRX**（1 TRX = 1,000,000 Sun）
- 低階 API（`ITronProvider`、`TransactionBuilder`）：使用 `long`，單位為 **Sun**
- SDK 內部自動轉換，使用者不需要手動換算

---

## 專案結構

```
src/
├── ChainKit.Core/                         → 跨鏈共用（介面、工具、Result Pattern）
└── ChainKit.Tron/                         → 所有 Tron 功能
    ├── Crypto/                            → 地址、金鑰、簽章、ABI 編碼
    │   ├── TronAccount.cs
    │   ├── Mnemonic.cs
    │   ├── TronAddress.cs
    │   ├── TronSigner.cs
    │   ├── AbiEncoder.cs
    │   └── Keccak256.cs
    ├── Protocol/                          → Protobuf 定義、交易構建與序列化
    │   ├── Protobuf/                      → .proto 編譯出的 C# 類別
    │   ├── TransactionBuilder.cs
    │   └── TransactionUtils.cs
    ├── Providers/                          → Provider 抽象 + HTTP/gRPC 實作
    │   ├── ITronProvider.cs
    │   ├── TronHttpProvider.cs
    │   ├── TronGrpcProvider.cs
    │   └── TronNetwork.cs
    ├── Contracts/                          → TRC20 合約管理
    │   ├── Trc20Contract.cs
    │   ├── Trc20Template.cs               → 內建 TRC20 Solidity bytecode
    │   └── TokenInfoCache.cs              → 三層代幣資訊快取
    ├── Watching/                           → 交易監聽
    │   ├── ITronBlockStream.cs
    │   ├── ZmqBlockStream.cs
    │   ├── PollingBlockStream.cs
    │   └── TronTransactionWatcher.cs
    ├── Models/                             → 所有 DTO、Result、Enums
    │   ├── TronResult.cs
    │   ├── TransactionModels.cs
    │   ├── AccountModels.cs
    │   ├── ResourceModels.cs
    │   └── WatcherModels.cs
    └── TronClient.cs                      → 高階 Facade

tests/
├── ChainKit.Core.Tests/
└── ChainKit.Tron.Tests/
    ├── Crypto/                            → 離線功能測試（已知測試向量）
    ├── Protocol/                          → 交易構建測試
    ├── Providers/                         → Mock Provider 測試
    ├── Contracts/                         → TRC20 合約測試
    ├── Watching/                          → 監聽功能測試
    └── Integration/                       → Nile 測試網 E2E 測試
```

### 依賴關係

```
內部系統 → install ChainKit.Tron → 自動帶入 ChainKit.Core
```

未來新增其他鏈：`install ChainKit.Ethereum` → 自動帶入 `ChainKit.Core`。

---

## 依賴套件

| 套件 | 版本 | 用途 | 授權 |
|------|------|------|------|
| NBitcoin.Secp256k1 | 3.2.0 | ECDSA secp256k1 簽章 | MIT |
| NBitcoin | 9.0.5 | BIP39 助記詞 / BIP44 HD 推導 | MIT |
| Google.Protobuf | 3.34.1 | Protobuf 序列化 | BSD |
| Grpc.Net.Client | 2.76.0 | gRPC 客戶端 | MIT |
| Grpc.Tools | 2.80.0 | .proto 編譯（build only） | Apache 2.0 |
| NetMQ | 4.0.2.2 | ZMQ 監聽 | MPL 2.0 |
| Keccak-256 | 自行實作 | 地址 / ABI 雜湊 | — |

全部純 C#（managed），跨平台。目標框架為 `net10.0`（內部使用，所有消費者均為 .NET 10）。

### 加密庫選擇理由

- **NBitcoin.Secp256k1**：Nethereum 和 TronNet 都用它，比 BouncyCastle 快 20-100x
- **不用 BouncyCastle**：太重，且 EC 效能差
- **不用 Secp256k1.Net**：需要 native binary，不利於套件分發
- **Keccak-256 自行實作**：只需約 200 行 C#，避免為一個 hash 拉整個 BouncyCastle
- **注意**：.NET 內建 `SHA3_256` 是 NIST SHA3（padding `0x06`），不是 Tron/Ethereum 用的 Keccak-256（padding `0x01`），不能混用

### Proto 檔案來源

https://github.com/tronprotocol/protocol — 主要使用 `core/Tron.proto` 和 `core/contract/` 目錄下的定義。

---

## ChainKit.Core — 跨鏈共用

零外部依賴。放各鏈一定會共用的東西，不過度抽象。

### Result Pattern

```csharp
public record ChainResult<T>
{
    public bool Success { get; }
    public T? Data { get; }
    public ChainError? Error { get; }

    public static ChainResult<T> Ok(T data);
    public static ChainResult<T> Fail(ChainError error);
}

public record ChainError(
    string Code,              // 各鏈自定義錯誤碼（如 "INSUFFICIENT_BALANCE"）
    string Message,           // 人類可讀描述
    string? RawMessage);      // 節點原始訊息（debug 用）
```

### 通用介面

```csharp
public interface IAccount
{
    string Address { get; }
    byte[] PublicKey { get; }
}

public interface ITransaction
{
    string TxId { get; }
    string FromAddress { get; }
    string ToAddress { get; }
    decimal Amount { get; }
    DateTimeOffset Timestamp { get; }
}
```

### 共用工具

```csharp
public static class HexExtensions
{
    public static string ToHex(this byte[] bytes);
    public static byte[] FromHex(this string hex);
}

public static class Base58Extensions
{
    public static string ToBase58Check(this byte[] bytes);
    public static byte[] FromBase58Check(this string encoded);
}
```

### 共用例外

```csharp
// 僅 SDK 內部 bug 時 throw，使用者不需要 catch
public class ChainKitException : Exception { ... }
```

---

## ChainKit.Tron — Crypto（離線功能）

`ChainKit.Tron.Crypto` namespace。純計算，零網路依賴。

### 帳戶管理

```csharp
public class TronAccount : IAccount
{
    public string Address { get; }          // T 開頭的 Base58Check 地址
    public string HexAddress { get; }       // 41 開頭的 Hex 地址
    public byte[] PublicKey { get; }
    public byte[] PrivateKey { get; }

    public static TronAccount Create();                                      // 隨機生成
    public static TronAccount FromPrivateKey(byte[] privateKey);             // 從私鑰恢復
    public static TronAccount FromMnemonic(string mnemonic, int index = 0);  // BIP44 推導
}
```

BIP44 推導路徑：`m/44'/195'/0'/0/{index}`（195 是 Tron 的 coin type）。

### 助記詞

```csharp
public static class Mnemonic
{
    public static string Generate(int wordCount = 12);                       // 12 或 24 字
    public static byte[] ToSeed(string mnemonic, string passphrase = "");    // BIP39 seed
    public static bool Validate(string mnemonic);
}
```

### 地址工具

```csharp
public static class TronAddress
{
    public static bool IsValid(string address);                  // 驗證地址格式（Base58 或 Hex）
    public static string ToBase58(string hexAddress);            // 41... → T...
    public static string ToHex(string base58Address);            // T... → 41...
}
```

地址生成流程：私鑰 → ECDSA 公鑰 → Keccak256 → 取後 20 bytes → 加 `0x41` 前綴 → Base58Check 編碼。

### 簽章

```csharp
public static class TronSigner
{
    public static byte[] Sign(byte[] rawData, byte[] privateKey);
    public static bool Verify(byte[] rawData, byte[] signature, byte[] publicKey);
}
```

### ABI 編碼

EVM 相容的 ABI 編碼，用於 TRC20 合約互動。

```csharp
public static class AbiEncoder
{
    // Function selector：Keccak256(signature) 取前 4 bytes
    public static byte[] EncodeFunctionSelector(string signature);

    // 參數編碼（左補零到 32 bytes）
    public static byte[] EncodeAddress(string hexAddress);
    public static byte[] EncodeUint256(BigInteger value);

    // 常用函數的完整編碼（selector + params）
    public static byte[] EncodeTransfer(string toHex, BigInteger amount);
    public static byte[] EncodeBalanceOf(string addressHex);
    public static byte[] EncodeApprove(string spenderHex, BigInteger amount);
    public static byte[] EncodeMint(string toHex, BigInteger amount);
    public static byte[] EncodeBurn(BigInteger amount);

    // 解碼回傳值
    public static BigInteger DecodeUint256(byte[] data);
    public static string DecodeAddress(byte[] data);
    public static string DecodeString(byte[] data);
}
```

### 單位轉換工具

```csharp
public static class TronConverter
{
    public static decimal SunToTrx(long sun);                              // Sun → TRX
    public static long TrxToSun(decimal trx);                              // TRX → Sun
    public static decimal ToTokenAmount(BigInteger rawAmount, int decimals); // raw → 人類可讀
    public static BigInteger ToRawAmount(decimal amount, int decimals);      // 人類可讀 → raw
}
```

供使用低階 API 的用戶轉換單位。高階 API 已自動處理。

---

## ChainKit.Tron — Protocol（交易構建）

`ChainKit.Tron.Protocol` namespace。Tron 官方 `.proto` 編譯成 C# 類別。

### 交易構建

```csharp
public class TransactionBuilder
{
    // 建立各類交易
    public TransactionBuilder CreateTransfer(string fromHex, string toHex, long amountSun);
    public TransactionBuilder CreateTriggerSmartContract(
        string ownerHex, string contractHex, byte[] data, long callValue = 0);
    public TransactionBuilder CreateFreezeBalanceV2(string ownerHex, long amountSun, ResourceCode resource);
    public TransactionBuilder CreateUnfreezeBalanceV2(string ownerHex, long amountSun, ResourceCode resource);
    public TransactionBuilder CreateDelegateResource(
        string ownerHex, string receiverHex, long amountSun, ResourceCode resource, bool lockPeriod = false);
    public TransactionBuilder CreateUndelegateResource(
        string ownerHex, string receiverHex, long amountSun, ResourceCode resource);
    public TransactionBuilder CreateDeployContract(
        string ownerHex, byte[] bytecode, string abi, long callValue = 0);

    // 設定交易參數
    public TransactionBuilder SetReference(byte[] refBlockBytes, byte[] refBlockHash, long expiration);
    public TransactionBuilder SetFeeLimit(long feeLimit);
    public TransactionBuilder SetMemo(string memo);

    // 建構
    public Transaction Build();
}
```

### 交易工具

```csharp
public static class TransactionUtils
{
    public static byte[] ComputeTxId(Transaction transaction);              // SHA256(rawData)
    public static Transaction Sign(Transaction transaction, byte[] privateKey);
    public static Transaction AddSignature(Transaction transaction, byte[] signature);
}
```

---

## ChainKit.Tron — Providers（節點互動）

`ChainKit.Tron.Providers` namespace。

### 網路配置

```csharp
public static class TronNetwork
{
    public static readonly TronNetworkConfig Mainnet = new(
        HttpEndpoint: "https://api.trongrid.io",
        GrpcFullNodeEndpoint: "grpc.trongrid.io:50051",
        GrpcSolidityEndpoint: "grpc.trongrid.io:50061");

    public static readonly TronNetworkConfig Nile = new(
        HttpEndpoint: "https://nile.trongrid.io",
        GrpcFullNodeEndpoint: "grpc.nile.trongrid.io:50051",
        GrpcSolidityEndpoint: "grpc.nile.trongrid.io:50061");

    public static readonly TronNetworkConfig Shasta = new(
        HttpEndpoint: "https://api.shasta.trongrid.io",
        GrpcFullNodeEndpoint: "grpc.shasta.trongrid.io:50051",
        GrpcSolidityEndpoint: "grpc.shasta.trongrid.io:50061");
}

public record TronNetworkConfig(
    string HttpEndpoint,
    string GrpcFullNodeEndpoint,
    string? GrpcSolidityEndpoint = null);   // optional，沒提供則無法查確認狀態
```

HTTP Provider 透過同一個 base URL 同時存取 Full Node (`/wallet/...`) 和 Solidity Node (`/walletsolidity/...`)，使用者不需要分別配置。

gRPC Provider 的 Solidity endpoint 為 optional：
- 有提供 → `GetTransactionDetailAsync` 可回傳完整狀態（Confirmed / Failed）
- 沒提供 → 只能回傳 NotFound / Unconfirmed

### Provider 介面（低階 API）

直接對應 Tron 節點 API，一對一映射。回傳原生型別，錯誤時 throw exception。

```csharp
public interface ITronProvider
{
    // 帳戶
    Task<AccountInfo> GetAccountAsync(string address, CancellationToken ct = default);

    // 區塊
    Task<BlockInfo> GetNowBlockAsync(CancellationToken ct = default);
    Task<BlockInfo> GetBlockByNumAsync(long num, CancellationToken ct = default);

    // 交易（Full Node）
    Task<Transaction> CreateTransactionAsync(Transaction transaction, CancellationToken ct = default);
    Task<BroadcastResult> BroadcastTransactionAsync(Transaction signedTx, CancellationToken ct = default);
    Task<TransactionInfoDto> GetTransactionByIdAsync(string txId, CancellationToken ct = default);

    // 交易回執（Solidity Node）
    Task<TransactionInfoDto> GetTransactionInfoByIdAsync(string txId, CancellationToken ct = default);

    // 帳戶交易歷史（TronGrid V1 API）
    Task<IReadOnlyList<TransactionInfoDto>> GetAccountTransactionsAsync(
        string address, int limit = 10, CancellationToken ct = default);

    // 智能合約
    Task<Transaction> TriggerSmartContractAsync(
        string ownerAddress, string contractAddress,
        string functionSelector, byte[] parameter,
        long feeLimit, long callValue = 0,
        CancellationToken ct = default);
    Task<byte[]> TriggerConstantContractAsync(
        string ownerAddress, string contractAddress,
        string functionSelector, byte[] parameter,
        CancellationToken ct = default);

    // 資源
    Task<AccountResourceInfo> GetAccountResourceAsync(string address, CancellationToken ct = default);
    Task<long> EstimateEnergyAsync(
        string ownerAddress, string contractAddress,
        string functionSelector, byte[] parameter,
        CancellationToken ct = default);

    // 委託資源查詢
    Task<DelegatedResourceIndex> GetDelegatedResourceAccountIndexAsync(
        string address, CancellationToken ct = default);
    Task<IReadOnlyList<DelegatedResourceInfo>> GetDelegatedResourceAsync(
        string fromAddress, string toAddress, CancellationToken ct = default);
}

// 兩個實作，均實作 IDisposable
public class TronHttpProvider : ITronProvider, IDisposable
{
    public TronHttpProvider(string baseUrl, string? apiKey = null);
    public TronHttpProvider(TronNetworkConfig network, string? apiKey = null);
    // HTTP 透過同一個 base URL 自動存取 Full Node + Solidity Node
}

public class TronGrpcProvider : ITronProvider, IDisposable
{
    public TronGrpcProvider(string fullNodeEndpoint, string? solidityEndpoint = null);
    public TronGrpcProvider(TronNetworkConfig network);
    // solidityEndpoint 為 optional，沒提供則交易查詢無法取得確認狀態
    // 預設使用 HTTPS 連線
}
```

### 低階 DTO

```csharp
public record AccountInfo(
    string Address, long Balance,
    long NetUsage, long EnergyUsage,
    long CreateTime,
    long FrozenBalanceForBandwidth = 0,   // Staking 2.0 frozen amount
    long FrozenBalanceForEnergy = 0);

public record BlockInfo(
    long BlockNumber, string BlockId,
    long Timestamp, int TransactionCount,
    byte[] BlockHeaderRawData,
    IReadOnlyList<BlockTransactionInfo>? Transactions = null);

public record BlockTransactionInfo(
    string TxId, string ContractType,
    string OwnerAddress, string ToAddress,
    long Amount, string? ContractAddress, byte[]? Data);

public record BroadcastResult(
    bool Success, string? TxId, string? Message);

public record TransactionInfoDto(
    string TxId, long BlockNumber, long BlockTimestamp,
    string ContractResult, long Fee,
    long EnergyUsage, long NetUsage,
    long EnergyFee = 0, long NetFee = 0,             // TRX costs in Sun
    string? ContractType = null,                       // parsed from raw_data
    string? OwnerAddress = null, string? ToAddress = null,
    long AmountSun = 0, string? ContractAddress = null,
    byte[]? ContractData = null);

public record AccountResourceInfo(
    long FreeBandwidthLimit, long FreeBandwidthUsed,
    long EnergyLimit, long EnergyUsed,
    long TotalBandwidthLimit, long TotalBandwidthUsed);

public record DelegatedResourceIndex(
    IReadOnlyList<string> ToAddresses,
    IReadOnlyList<string> FromAddresses);

public record DelegatedResourceInfo(
    string From, string To,
    long FrozenBalanceForBandwidth,
    long FrozenBalanceForEnergy);
```

---

## ChainKit.Tron — TronClient（高階 Facade）

提供一步到位的高階 API，內部整合多個低階呼叫。

### 錯誤處理

所有高階 API 回傳 `TronResult<T>`（繼承自 `ChainResult<T>`）。

```csharp
public record TronResult<T> : ChainResult<T>
{
    public static TronResult<T> Ok(T data);
    public static TronResult<T> Fail(TronErrorCode code, string message, string? nodeMessage = null);
}

public enum TronErrorCode
{
    Unknown,
    InvalidAddress,
    InsufficientBalance,
    InsufficientEnergy,
    InsufficientBandwidth,
    ContractExecutionFailed,
    ContractValidationFailed,
    TransactionExpired,
    DuplicateTransaction,
    ProviderConnectionFailed,
    ProviderTimeout
}
```

使用方式：
```csharp
var result = await client.TransferTrxAsync(account, toAddress, 100m);
if (result.Success)
    Console.WriteLine($"TxId: {result.Data.TxId}");
else
    Console.WriteLine($"Error: {result.Error.Message}");
```

不需要 try-catch。只有 SDK 內部 bug 才會 throw。

### 輸入驗證

所有接受 `decimal trxAmount` 的方法都會驗證：
- `trxAmount <= 0` → 回傳 `Fail(InvalidAddress, "Amount must be positive")`
- 溢位（超過 long 範圍）→ 回傳 `Fail(InvalidAddress, "Amount too large")`

使用 `checked` 算術防止靜默溢位。

### 數值保證

- **TRX 金額**：`decimal`，單位 TRX，SDK 內部精確轉換（`decimal` 乘法無浮點誤差）
- **Token 金額**：同時提供 `RawAmount`（永遠正確的原始值）和 `Amount?`（轉換後的值，null = 無法轉換）
- **手續費**：`decimal` TRX，從 receipt 的 `energy_fee` + `net_fee` 解析
- **Energy/Bandwidth**：`long`，raw 值不需轉換

### 資源清理

`TronClient` 和 `TronHttpProvider` 均實作 `IDisposable`。使用完畢需 dispose 以避免 socket 洩漏。

### TronClient

```csharp
public class TronClient : IDisposable
{
    public TronClient(ITronProvider provider);

    // 使用者也可以直接存取低階 Provider
    public ITronProvider Provider { get; }

    // === 轉帳 ===

    // 一步完成：建交易 → 取 ref block → 簽章 → 廣播 → 回傳結果
    Task<TronResult<TransferResult>> TransferTrxAsync(
        TronAccount from, string toAddress, decimal trxAmount);

    // 一步完成：encode 參數 → trigger contract → 簽章 → 廣播
    Task<TronResult<TransferResult>> TransferTrc20Async(
        TronAccount from, string contractAddress,
        string toAddress, decimal amount, int decimals);

    // === 查詢 ===

    // 合併 Full Node + Solidity Node 結果，回傳完整交易資訊
    Task<TronResult<TronTransactionDetail>> GetTransactionDetailAsync(string txId);

    // TRX + 多個 TRC20 餘額一次查
    Task<TronResult<BalanceInfo>> GetBalanceAsync(
        string address, params string[] trc20Contracts);

    // 帳戶總覽：餘額 + 資源 + 近期交易
    Task<TronResult<AccountOverview>> GetAccountOverviewAsync(string address);

    // === 資源管理（Staking 2.0） ===

    Task<TronResult<StakeResult>> StakeTrxAsync(
        TronAccount account, decimal trxAmount, ResourceType resource);

    Task<TronResult<UnstakeResult>> UnstakeTrxAsync(
        TronAccount account, decimal trxAmount, ResourceType resource);

    Task<TronResult<DelegateResult>> DelegateResourceAsync(
        TronAccount account, string receiverAddress,
        decimal trxAmount, ResourceType resource,
        bool lockPeriod = false);

    Task<TronResult<UndelegateResult>> UndelegateResourceAsync(
        TronAccount account, string receiverAddress,
        decimal trxAmount, ResourceType resource);

    Task<TronResult<ResourceInfo>> GetResourceInfoAsync(string address);

    // === 合約部署 ===

    // 低階：部署任意合約（使用者提供 bytecode）
    Task<TronResult<DeployResult>> DeployContractAsync(
        TronAccount account, byte[] bytecode, string abi,
        long feeLimit, params object[] constructorArgs);

    // 高階：一鍵部署 TRC20（SDK 內建標準模板）
    Task<TronResult<DeployResult>> DeployTrc20TokenAsync(
        TronAccount account, Trc20TokenOptions options);

    // === TRC20 合約管理 ===

    // 取得 TRC20 合約操作物件
    Trc20Contract GetTrc20Contract(string contractAddress, TronAccount ownerAccount);
}
```

---

## ChainKit.Tron — Contracts（TRC20 合約管理）

`ChainKit.Tron.Contracts` namespace。

```csharp
public class Trc20Contract
{
    public string ContractAddress { get; }

    // --- 唯讀操作（不上鏈，不需簽章）---
    Task<TronResult<string>> NameAsync();
    Task<TronResult<string>> SymbolAsync();
    Task<TronResult<byte>> DecimalsAsync();
    Task<TronResult<decimal>> TotalSupplyAsync();
    Task<TronResult<decimal>> BalanceOfAsync(string address);
    Task<TronResult<decimal>> AllowanceAsync(string owner, string spender);

    // --- 寫入操作（上鏈，需簽章）---
    Task<TronResult<TransferResult>> TransferAsync(string to, decimal amount);
    Task<TronResult<TransferResult>> ApproveAsync(string spender, decimal amount);

    // --- Owner 操作（需要合約 owner 權限）---
    Task<TronResult<TransferResult>> MintAsync(string to, decimal amount);
    Task<TronResult<TransferResult>> BurnAsync(decimal amount);
    Task<TronResult<TransferResult>> BurnFromAsync(string from, decimal amount);
}
```

### Token 資訊快取（三層解析）

```csharp
public record TokenInfo(string Symbol, int Decimals);

public class TokenInfoCache
{
    // Layer 1: 內建已知代幣表（零延遲）
    // USDT: 41a614f803b6fd780986a42c78ec9c7f77e6ded13c → ("USDT", 6)

    // Layer 2: Memory cache（ConcurrentDictionary，零延遲）

    // Layer 3: 合約呼叫 symbol() + decimals()（首次需網路，結果存 cache）

    public TokenInfo? Get(string contractAddress);                    // Layer 1+2 only
    public Task<TokenInfo> GetOrResolveAsync(                        // Layer 1+2+3
        string contractAddress, ITronProvider provider, CancellationToken ct = default);
    public void Set(string contractAddress, TokenInfo info);          // 手動加入 cache
}
```

每個合約只查一次，之後永遠走 cache（symbol/decimals 不會變）。

### TRC20 部署

TRC20 部署：SDK 內建標準 Solidity bytecode 模板，`Trc20TokenOptions` 的 `Mintable` / `Burnable` 控制功能開關。進階使用者可用 `DeployContractAsync()` 部署自訂合約。

```csharp
public record Trc20TokenOptions(
    string Name,                    // "My Token"
    string Symbol,                  // "MTK"
    byte Decimals,                  // 18
    BigInteger InitialSupply,       // 初始發行量
    bool Mintable = true,
    bool Burnable = true);
```

---

## ChainKit.Tron — Models（回傳模型）

`ChainKit.Tron.Models` namespace。

### 轉帳結果

```csharp
public record TransferResult(
    string TxId,
    string FromAddress,
    string ToAddress,
    decimal Amount);
```

### 交易詳情

內部自動整合 Full Node (`getTransactionById`) + Solidity Node (`getTransactionInfoById`) 的結果。

```csharp
public record TronTransactionDetail(
    // --- 基本資訊 ---
    string TxId,
    string FromAddress,
    string ToAddress,

    // --- 狀態 ---
    // Full Node 查不到 → NotFound
    // Full Node 有，Solidity 沒有（或無 Solidity 連線）→ Unconfirmed
    // Solidity 有，contractRet = SUCCESS → Confirmed
    // Solidity 有，contractRet != SUCCESS → Failed
    TransactionStatus Status,
    FailureInfo? Failure,           // 只有 Status == Failed 時有值

    // --- 交易分類 ---
    TransactionType Type,

    // --- 金額 ---
    decimal Amount,                 // TRX 金額（NativeTransfer 時）
    TokenTransferInfo? TokenTransfer,  // TRC20/TRC10 時的代幣資訊

    // --- 區塊資訊 ---
    long? BlockNumber,              // NotFound 時為 null
    DateTimeOffset? Timestamp,

    // --- 資源消耗 ---
    ResourceCost? Cost);            // NotFound / Unconfirmed 時為 null

public enum TransactionStatus
{
    NotFound,                       // 查不到（未廣播、已過期、或 txId 錯誤）
    Unconfirmed,                    // Full Node 已收到，Solidity 尚未確認
    Confirmed,                      // Solidity 確認成功
    Failed                          // Solidity 確認失敗
}

public enum TransactionType
{
    NativeTransfer,                 // TRX 轉帳
    Trc20Transfer,                  // TRC20 代幣轉帳
    Trc10Transfer,                  // TRC10 代幣轉帳
    ContractCall,                   // 智能合約呼叫（非轉帳）
    ContractDeploy,                 // 合約部署
    Stake,                          // 質押
    Unstake,                        // 解除質押
    Delegate,                       // 委託
    Undelegate,                     // 解除委託
    Other                           // 其他
}

public record TokenTransferInfo(
    string ContractAddress,         // 合約地址
    string Symbol,                  // "USDT", "USDC", etc.（空字串 = 未知）
    int Decimals,                   // 6 (USDT), 18 (其他)，0 = 未知
    decimal RawAmount,              // 永遠正確的原始鏈上值（最小單位）
    decimal? Amount);               // 轉換後的人類可讀金額，null = decimals 未知無法轉換

public record ResourceCost(
    decimal TrxBurned,              // 燃燒的 TRX（手續費）
    long BandwidthUsed,
    long EnergyUsed,
    decimal BandwidthTrxCost,       // Bandwidth 不足時消耗的 TRX
    decimal EnergyTrxCost);         // Energy 不足時消耗的 TRX

public record FailureInfo(
    FailureReason Reason,
    string Message,                 // 人類可讀描述
    string? RevertMessage,          // 合約 revert message（如有，SDK 自動解碼）
    string? RawResult);             // 節點原始回傳（debug 用）

public enum FailureReason
{
    OutOfEnergy,
    OutOfBandwidth,
    InsufficientBalance,
    ContractReverted,               // 合約 revert（帶 revert message）
    ContractOutOfTime,              // 合約執行超時
    InvalidSignature,
    Expired,
    DuplicateTransaction,
    Other
}
```

### 帳戶與資源

```csharp
public record Trc20BalanceInfo(
    decimal RawBalance,             // 永遠正確的原始值
    decimal? Balance,               // 轉換後的金額，null = decimals 未知
    string Symbol,                  // 代幣符號
    int Decimals);                  // 代幣精度

public record BalanceInfo(
    decimal TrxBalance,
    IReadOnlyDictionary<string, Trc20BalanceInfo> Trc20Balances);  // contractAddress → balance info

public record AccountOverview(
    string Address,
    decimal TrxBalance,
    long Bandwidth,
    long BandwidthUsed,
    long Energy,
    long EnergyUsed,
    IReadOnlyList<TronTransactionDetail> RecentTransactions);

public enum ResourceType { Bandwidth, Energy }

public record ResourceInfo(
    long BandwidthTotal, long BandwidthUsed,
    long EnergyTotal, long EnergyUsed,
    decimal StakedForBandwidth,
    decimal StakedForEnergy,
    IReadOnlyList<DelegationInfo> DelegationsOut,
    IReadOnlyList<DelegationInfo> DelegationsIn);

public record DelegationInfo(
    string Address, decimal Amount,
    ResourceType Resource, bool Locked);

public record StakeResult(string TxId, decimal Amount, ResourceType Resource);
public record UnstakeResult(string TxId, decimal Amount, ResourceType Resource);
public record DelegateResult(string TxId, string ReceiverAddress, decimal Amount, ResourceType Resource);
public record UndelegateResult(string TxId, string ReceiverAddress, decimal Amount, ResourceType Resource);

public record DeployResult(string TxId, string ContractAddress);
```

---

## ChainKit.Tron — Watching（交易監聽）

`ChainKit.Tron.Watching` namespace。

```csharp
public interface ITronBlockStream
{
    IAsyncEnumerable<TronBlock> StreamBlocksAsync(CancellationToken ct = default);
}

public record TronBlock(
    long BlockNumber,
    string BlockId,
    DateTimeOffset Timestamp,
    IReadOnlyList<TronBlockTransaction> Transactions);

public record TronBlockTransaction(
    string TxId,
    string FromAddress,
    string ToAddress,
    string ContractType,
    byte[] RawData);
```

```csharp
// 兩種來源實作
public class ZmqBlockStream : ITronBlockStream
{
    public ZmqBlockStream(string zmqEndpoint);                            // 需自架節點
}

public class PollingBlockStream : ITronBlockStream
{
    public PollingBlockStream(ITronProvider provider, int intervalMs = 3000);  // 不需節點
}
```

```csharp
// 多地址監聽
public class TronTransactionWatcher : IAsyncDisposable
{
    public TronTransactionWatcher(ITronBlockStream stream, ITronProvider? provider = null);

    // 動態增減監聽地址
    public void WatchAddress(string address);
    public void WatchAddresses(IEnumerable<string> addresses);
    public void UnwatchAddress(string address);

    // 事件回調
    public event EventHandler<TrxReceivedEventArgs>? OnTrxReceived;
    public event EventHandler<Trc20ReceivedEventArgs>? OnTrc20Received;
    public event EventHandler<TransactionConfirmedEventArgs>? OnTransactionConfirmed;

    public Task StartAsync(CancellationToken ct = default);
    public Task StopAsync();
}

public record TrxReceivedEventArgs(
    string TxId, string FromAddress, string ToAddress,
    decimal Amount, long BlockNumber, DateTimeOffset Timestamp);

public record Trc20ReceivedEventArgs(
    string TxId, string FromAddress, string ToAddress,
    string ContractAddress, string Symbol,
    decimal RawAmount,              // 永遠正確的原始值
    decimal? Amount,                // 轉換後金額，null = decimals 未知
    int Decimals,                   // 代幣精度，0 = 未知
    long BlockNumber, DateTimeOffset Timestamp);

public record TransactionConfirmedEventArgs(
    string TxId, long BlockNumber, bool Success);
```

使用方式：
```csharp
// 有自架節點 → ZMQ（即時推送，適合大量地址）
var stream = new ZmqBlockStream("tcp://node:5555");

// 沒有節點 → Polling（定時輪詢 API，適合少量地址）
var stream = new PollingBlockStream(provider, intervalMs: 3000);

// 同一個 Watcher 介面
var watcher = new TronTransactionWatcher(stream);
watcher.WatchAddresses(walletAddresses);  // 支援萬級地址
watcher.OnTrxReceived += (s, e) => Console.WriteLine($"{e.ToAddress} 收到 {e.Amount} TRX");
await watcher.StartAsync();

// 動態新增
watcher.WatchAddress(newAddress);
```

內部用 `HashSet<string>` 存監聽地址，O(1) 查找。

---

## 測試策略

| 測試類型 | 目錄 | 說明 |
|----------|------|------|
| Crypto 單元測試 | `tests/.../Crypto/` | 已知測試向量（BIP39、secp256k1），100% 離線 |
| Protocol 單元測試 | `tests/.../Protocol/` | 交易構建 + Protobuf 序列化，用已知交易驗證 |
| Provider Mock 測試 | `tests/.../Providers/` | Mock HTTP/gRPC 回應，驗證解析邏輯 |
| 高階 API 整合測試 | `tests/.../` | Mock Provider，測試多步驟串接邏輯 |
| E2E 測試 | `tests/.../Integration/` | 連 Nile 測試網，`[Category("Integration")]`，CI 可選擇跳過 |

### 測試重點

- **地址生成**：用 Tron 官方測試向量驗證私鑰 → 公鑰 → 地址的完整推導
- **BIP39/BIP44**：用 BIP39 標準測試向量 + Tron 的 coin type 195 驗證
- **簽章**：用已知的 Tron 交易驗證簽章結果
- **ABI 編碼**：用已知的 TRC20 交易驗證 encode/decode
- **高階 API**：驗證 TransferTrxAsync 內部正確串接 建交易 → 取 ref block → 簽章 → 廣播
- **交易詳情**：驗證 Full Node + Solidity Node 合併邏輯，各種 Status 判斷
- **Result Pattern**：驗證各種錯誤場景回傳正確的 TronErrorCode
- **失敗資訊**：驗證 revert message 解碼、各 FailureReason 的正確映射
