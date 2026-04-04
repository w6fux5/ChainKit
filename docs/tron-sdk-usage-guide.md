# ChainKit Tron SDK 使用指南

## 安裝

```bash
dotnet add package ChainKit.Tron
```

SDK 會自動引入 `ChainKit.Core` 核心套件。

---

## 快速開始

5 行程式碼完成第一筆 TRX 轉帳：

```csharp
using ChainKit.Tron;
using ChainKit.Tron.Crypto;
using ChainKit.Tron.Providers;

var provider = new TronHttpProvider(TronNetwork.Nile);          // 測試網
var client = new TronClient(provider);
var account = TronAccount.FromPrivateKey(Convert.FromHexString("你的私鑰hex"));
var result = await client.TransferTrxAsync(account, "TRecipientAddress...", 10m);
Console.WriteLine(result.Success ? $"成功！TxId: {result.Data!.TxId}" : $"失敗：{result.Error!.Message}");
```

---

## 核心概念

### TronResult 錯誤處理

所有 `TronClient` 和 `Trc20Contract` 的方法都回傳 `TronResult<T>`，業務錯誤不會 throw exception：

```csharp
var result = await client.TransferTrxAsync(account, "TTo...", 100m);

if (result.Success)
{
    var data = result.Data!;
    Console.WriteLine($"成功: {data.TxId}");
}
else
{
    var error = result.Error!;
    Console.WriteLine($"失敗: [{error.Code}] {error.Message}");
}

// 錯誤物件結構
// result.Error.Code         - 錯誤碼字串（對應 TronErrorCode enum）
// result.Error.Message      - 人類可讀錯誤描述
// result.Error.RawMessage   - 節點原始訊息（debug 用，可能為 null）
```

#### 錯誤碼列表

| TronErrorCode | 說明 |
|---------------|------|
| `Unknown` | 未知錯誤 |
| `InvalidAddress` | 地址格式無效 |
| `InvalidAmount` | 金額無效（負數、零、overflow） |
| `InsufficientBalance` | TRX 餘額不足 |
| `InsufficientEnergy` | 能量不足 |
| `InsufficientBandwidth` | 頻寬不足 |
| `ContractExecutionFailed` | 合約執行失敗 |
| `ContractValidationFailed` | 合約驗證失敗 |
| `TransactionExpired` | 交易已過期 |
| `DuplicateTransaction` | 重複交易 |
| `ProviderConnectionFailed` | 節點連線失敗（網路錯誤） |
| `ProviderTimeout` | 節點連線逾時 |

### 金額與單位

SDK 涉及三種金額場景，規則如下：

#### TRX 金額

| API 層級 | 單位 | 類型 | 範例 |
|----------|------|------|------|
| 高階（TronClient） | TRX | `decimal` | `10m` = 10 TRX |
| 低階（ITronProvider / TransactionBuilder） | Sun | `long` | `10_000_000` = 10 TRX |

換算：1 TRX = 1,000,000 Sun

```csharp
using ChainKit.Tron.Crypto;

// Sun ↔ TRX 互轉
decimal trx = TronConverter.SunToTrx(1_500_000);    // 1.5m
long sun = TronConverter.TrxToSun(1.5m);             // 1_500_000
```

#### TRC20 代幣金額：RawAmount vs Amount

SDK 中所有涉及 TRC20 金額的回傳都同時提供兩個欄位：

| 欄位 | 類型 | 說明 | 永遠有值？ |
|------|------|------|-----------|
| `RawAmount` / `RawBalance` | `decimal` | 鏈上原始值（最小單位） | 是 |
| `Amount` / `Balance` | `decimal?` | 經 decimals 轉換的人類可讀值 | 否 |

**`Amount` / `Balance` 何時為 null？** 當 SDK 無法取得該代幣的 decimals 時（例如合約呼叫失敗），無法轉換，因此為 `null`。

```csharp
// 範例：USDT（6 decimals）
// RawAmount = 1000000m    ← 永遠有值
// Amount    = 1.0m        ← 1000000 / 10^6

// 如果 decimals 未知
// RawAmount = 1000000m    ← 永遠有值
// Amount    = null        ← 無法轉換
```

#### Consumer 如何自行轉換

當 `Amount` / `Balance` 為 `null`，或你需要從 raw 值手動轉換時，使用 `TronConverter`：

```csharp
using System.Numerics;
using ChainKit.Tron.Crypto;

// Raw → 人類可讀（已知 decimals）
decimal amount = TronConverter.ToTokenAmount(1_000_000, decimals: 6);   // 1.0

// 人類可讀 → Raw（例如要自行建構交易時）
BigInteger raw = TronConverter.ToRawAmount(1.0m, decimals: 6);          // 1000000
```

> **注意：** 高階 API（`TronClient`）的輸入參數接受人類可讀金額，SDK 內部自動轉換。`TronConverter` 主要給使用低階 API 或需要自行處理 raw 值的場景。

### 高階 API vs 低階 API

#### 高階 API（TronClient）

`TronClient` 是門面類別，一個方法完成一件事。內部自動處理：取 ref block、建交易、簽章、廣播。

- 金額使用 `decimal`，單位為 **TRX**
- 回傳 `TronResult<T>`，不需要 try-catch
- 適合 90% 的使用場景

```csharp
var client = new TronClient(provider);
var result = await client.TransferTrxAsync(account, "TToAddress...", 100m);
```

#### 低階 API（ITronProvider + TransactionBuilder）

直接對應 Tron 節點 API，適合需要完全控制交易構建流程的場景。

- 金額使用 `long`（Sun）/ `BigInteger`（token raw），需自行轉換
- 需要手動處理 ref block、簽章、廣播
- 可能會 throw exception

```csharp
var block = await provider.GetNowBlockAsync();
var accountInfo = await provider.GetAccountAsync("TAddress...");
var tx = await provider.TriggerSmartContractAsync(
    ownerAddress, contractAddress,
    "transfer(address,uint256)", parameterBytes,
    feeLimit: 100_000_000);
var broadcastResult = await provider.BroadcastTransactionAsync(signedTx);
```

#### 選擇指引

| 場景 | 推薦 API | 原因 |
|------|----------|------|
| TRX 轉帳 | `TronClient.TransferTrxAsync` | 一步到位 |
| TRC20 轉帳 | `TronClient.TransferTrc20Async` | 自動處理 ABI 編碼 |
| 查餘額 | `TronClient.GetBalanceAsync` | 自動轉換單位 |
| 質押/委託 | `TronClient` 各方法 | 自動處理 Stake 2.0 |
| 批次查詢帳戶資訊 | `ITronProvider.GetAccountAsync` | 直接拿 Sun 值避免反覆轉換 |
| 自訂合約呼叫 | `ITronProvider.TriggerSmartContractAsync` | 完全控制 ABI 參數 |
| 唯讀合約查詢 | `ITronProvider.TriggerConstantContractAsync` | 不需簽章 |
| 需要精確控制 fee limit | `ITronProvider` + `TransactionBuilder` | 高階 API 使用預設 100 TRX |

---

## Provider 選擇

所有操作都需要一個 `ITronProvider` 來連接 Tron 節點。SDK 提供兩種實作。

### HTTP Provider（推薦）

透過 TronGrid REST API 通訊，最簡單的方式，適合大多數場景：

```csharp
using ChainKit.Tron.Providers;

// 使用預設 TronGrid 節點
var provider = new TronHttpProvider(TronNetwork.Mainnet);

// 使用 API Key（推薦，提高速率限制）
var provider = new TronHttpProvider(TronNetwork.Mainnet, apiKey: "你的TronGrid-API-Key");

// 使用自訂節點 URL
var provider = new TronHttpProvider("https://your-node.example.com");
var provider = new TronHttpProvider("https://your-node.example.com", apiKey: "your-key");
```

#### 雙端點設定（Full Node + Solidity Node）

交易查詢和確認追蹤需要 Solidity Node。`TronHttpProvider` 支援分別指定：

```csharp
// Full Node 和 Solidity Node 使用不同端點
var provider = new TronHttpProvider(
    baseUrl: "https://your-fullnode.example.com",
    solidityUrl: "https://your-solidity.example.com");

// 不指定 solidityUrl 時，預設使用 baseUrl（TronGrid 同時支援兩者）
```

### gRPC Provider

透過 gRPC 通訊，適合需要低延遲或自架節點的場景：

```csharp
using ChainKit.Tron.Providers;

// 使用預設 TronGrid gRPC 節點
var provider = new TronGrpcProvider(TronNetwork.Mainnet);

// 使用自訂節點
var provider = new TronGrpcProvider("grpc.your-node.com:50051");

// 指定 Full Node 和 Solidity Node
var provider = new TronGrpcProvider(
    fullNodeEndpoint: "grpc.your-node.com:50051",
    solidityEndpoint: "grpc.your-node.com:50061");
```

### 自訂網路設定

使用 `TronNetworkConfig` 可以同時指定 HTTP 和 gRPC 端點：

```csharp
var customNetwork = new TronNetworkConfig(
    HttpEndpoint: "https://your-node.example.com",
    GrpcFullNodeEndpoint: "grpc.your-node.com:50051",
    GrpcSolidityEndpoint: "grpc.your-node.com:50061");

var httpProvider = new TronHttpProvider(customNetwork);
var grpcProvider = new TronGrpcProvider(customNetwork);
```

### 網路選擇（Mainnet / Nile / Shasta）

SDK 內建三個網路設定：

| 網路 | 用途 | 設定 |
|------|------|------|
| Mainnet | 正式環境 | `TronNetwork.Mainnet` |
| Nile | 測試網（推薦開發用） | `TronNetwork.Nile` |
| Shasta | 測試網 | `TronNetwork.Shasta` |

```csharp
// 各網路的端點一覽
// Mainnet:  HTTP https://api.trongrid.io       gRPC grpc.trongrid.io:50051
// Nile:     HTTP https://nile.trongrid.io      gRPC grpc.nile.trongrid.io:50051
// Shasta:   HTTP https://api.shasta.trongrid.io gRPC grpc.shasta.trongrid.io:50051
```

---

## 地址與金鑰管理

### 建立新帳戶

```csharp
using ChainKit.Tron.Crypto;

// 隨機產生新帳戶
var account = TronAccount.Create();

Console.WriteLine($"地址（Base58）: {account.Address}");      // T 開頭
Console.WriteLine($"地址（Hex）:    {account.HexAddress}");   // 41 開頭
Console.WriteLine($"私鑰:          {Convert.ToHexString(account.PrivateKey)}");
Console.WriteLine($"公鑰:          {Convert.ToHexString(account.PublicKey)}");
```

### 從私鑰恢復

```csharp
var privateKeyHex = "your_64_hex_chars_private_key_here_...";
var account = TronAccount.FromPrivateKey(Convert.FromHexString(privateKeyHex));

Console.WriteLine($"地址: {account.Address}");
```

### 從助記詞恢復

```csharp
// 產生助記詞
var mnemonic = Mnemonic.Generate(12);        // 12 個英文單字
Console.WriteLine($"助記詞: {mnemonic}");

// 驗證助記詞
var isValid = Mnemonic.Validate(mnemonic);   // true/false

// 從助記詞恢復帳戶（BIP44 路徑: m/44'/195'/0'/0/{index}）
var account = TronAccount.FromMnemonic(mnemonic, index: 0);  // 第一個帳戶
var account2 = TronAccount.FromMnemonic(mnemonic, index: 1); // 第二個帳戶

// 產生 24 個單字的助記詞
var longMnemonic = Mnemonic.Generate(24);

// 助記詞轉 seed（帶 passphrase）
var seed = Mnemonic.ToSeed(mnemonic, passphrase: "my-password");
```

### 地址格式轉換

Tron 地址有兩種格式：Base58（`T` 開頭）和 Hex（`41` 開頭）。

```csharp
using ChainKit.Tron.Crypto;

// Base58 → Hex
var hex = TronAddress.ToHex("TJRabPrwbZy45sbavfcjinPJC18kjpRTv8");
// 結果: "41..." (42 個 hex 字元)

// Hex → Base58
var base58 = TronAddress.ToBase58("41a614f803b6fd780986a42c78ec9c7f77e6ded13c");
// 結果: "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t"

// 驗證地址
var valid = TronAddress.IsValid("TJRabPrwbZy45sbavfcjinPJC18kjpRTv8");  // true
var valid2 = TronAddress.IsValid("41a614f803b6fd780986a42c78ec9c7f77e6ded13c"); // true
var invalid = TronAddress.IsValid("not_an_address");                    // false
```

---

## 查詢

### 查詢餘額（TRX + TRC20）

```csharp
// 只查 TRX 餘額
var result = await client.GetBalanceAsync("TYourAddress...");

// 同時查 TRX 和多個 TRC20 餘額
var result = await client.GetBalanceAsync(
    "TYourAddress...",
    "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",   // USDT
    "TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8");   // 另一個 TRC20

if (result.Success)
{
    var balance = result.Data!;
    Console.WriteLine($"TRX 餘額: {balance.TrxBalance} TRX");

    foreach (var (contract, trc20) in balance.Trc20Balances)
    {
        Console.WriteLine($"合約 {contract}:");
        Console.WriteLine($"  符號:     {trc20.Symbol}");
        Console.WriteLine($"  小數位:   {trc20.Decimals}");
        Console.WriteLine($"  原始餘額: {trc20.RawBalance}");   // 最小單位，永遠有值
        Console.WriteLine($"  餘額:     {trc20.Balance}");      // 轉換後，可能為 null

        // 如果 Balance 為 null，可自行轉換：
        if (trc20.Balance is null && trc20.Decimals > 0)
        {
            var converted = TronConverter.ToTokenAmount(
                (System.Numerics.BigInteger)trc20.RawBalance, trc20.Decimals);
            Console.WriteLine($"  手動轉換: {converted}");
        }
    }
}
```

### 帳戶總覽

一次查詢帳戶的 TRX 餘額、資源使用、最近交易：

```csharp
var result = await client.GetAccountOverviewAsync("TYourAddress...");

if (result.Success)
{
    var overview = result.Data!;
    Console.WriteLine($"地址:       {overview.Address}");
    Console.WriteLine($"TRX 餘額:   {overview.TrxBalance} TRX");
    Console.WriteLine($"頻寬:       {overview.BandwidthUsed} / {overview.Bandwidth}");
    Console.WriteLine($"能量:       {overview.EnergyUsed} / {overview.Energy}");

    Console.WriteLine($"\n最近 {overview.RecentTransactions.Count} 筆交易:");
    foreach (var tx in overview.RecentTransactions)
    {
        Console.WriteLine($"  {tx.TxId[..16]}... [{tx.Type}] {tx.Status} {tx.Amount} TRX");
    }
}
```

### 查詢資源狀態

```csharp
var result = await client.GetResourceInfoAsync("TYourAddress...");

if (result.Success)
{
    var info = result.Data!;
    Console.WriteLine($"頻寬:     {info.BandwidthUsed} / {info.BandwidthTotal}");
    Console.WriteLine($"能量:     {info.EnergyUsed} / {info.EnergyTotal}");
    Console.WriteLine($"質押頻寬: {info.StakedForBandwidth} TRX");
    Console.WriteLine($"質押能量: {info.StakedForEnergy} TRX");

    Console.WriteLine($"\n委託出去（{info.DelegationsOut.Count} 筆）:");
    foreach (var d in info.DelegationsOut)
        Console.WriteLine($"  → {d.Address}: {d.Amount} TRX ({d.Resource})");

    Console.WriteLine($"\n收到委託（{info.DelegationsIn.Count} 筆）:");
    foreach (var d in info.DelegationsIn)
        Console.WriteLine($"  ← {d.Address}: {d.Amount} TRX ({d.Resource})");
}
```

### 查詢交易詳情

```csharp
var result = await client.GetTransactionDetailAsync("交易ID...");

if (result.Success)
{
    var detail = result.Data!;
    Console.WriteLine($"交易 ID:  {detail.TxId}");
    Console.WriteLine($"狀態:     {detail.Status}");       // Confirmed / Unconfirmed / Failed
    Console.WriteLine($"類型:     {detail.Type}");         // NativeTransfer / Trc20Transfer / ...
    Console.WriteLine($"從:       {detail.FromAddress}");
    Console.WriteLine($"到:       {detail.ToAddress}");
    Console.WriteLine($"金額:     {detail.Amount}");
    Console.WriteLine($"區塊號:   {detail.BlockNumber}");
    Console.WriteLine($"時間:     {detail.Timestamp}");

    // TRC20 轉帳會附帶代幣資訊
    if (detail.TokenTransfer is not null)
    {
        var token = detail.TokenTransfer;
        Console.WriteLine($"代幣符號:   {token.Symbol}");
        Console.WriteLine($"代幣小數:   {token.Decimals}");
        Console.WriteLine($"原始金額:   {token.RawAmount}");     // 最小單位，永遠有值
        Console.WriteLine($"可讀金額:   {token.Amount}");        // 轉換後，可能為 null
    }

    // 交易消耗的資源
    if (detail.Cost is not null)
    {
        var cost = detail.Cost;
        Console.WriteLine($"消耗 TRX:     {cost.TrxBurned} TRX");
        Console.WriteLine($"消耗頻寬:     {cost.BandwidthUsed}");
        Console.WriteLine($"消耗能量:     {cost.EnergyUsed}");
        Console.WriteLine($"頻寬 TRX:     {cost.BandwidthTrxCost} TRX");
        Console.WriteLine($"能量 TRX:     {cost.EnergyTrxCost} TRX");
    }

    // 失敗原因
    if (detail.Failure is not null)
    {
        Console.WriteLine($"失敗原因: {detail.Failure.Reason}");
        Console.WriteLine($"失敗訊息: {detail.Failure.Message}");
    }
}
```

---

## 轉帳

### TRX 轉帳（高階）

```csharp
using ChainKit.Tron;
using ChainKit.Tron.Crypto;
using ChainKit.Tron.Providers;

var provider = new TronHttpProvider(TronNetwork.Nile);
var client = new TronClient(provider);
var account = TronAccount.FromPrivateKey(Convert.FromHexString("your_private_key"));

// 轉帳 10 TRX
var result = await client.TransferTrxAsync(
    from: account,
    toAddress: "TRecipientBase58Address...",
    trxAmount: 10m);                          // 單位：TRX

if (result.Success)
{
    var tx = result.Data!;
    Console.WriteLine($"交易 ID:  {tx.TxId}");
    Console.WriteLine($"從:       {tx.FromAddress}");
    Console.WriteLine($"到:       {tx.ToAddress}");
    Console.WriteLine($"金額:     {tx.Amount} TRX");
}
else
{
    Console.WriteLine($"轉帳失敗: {result.Error!.Message}");
    Console.WriteLine($"錯誤碼:   {result.Error.Code}");
    if (result.Error.RawMessage != null)
        Console.WriteLine($"原始訊息: {result.Error.RawMessage}");
}
```

### TRX 轉帳（低階，完整控制）

```csharp
using ChainKit.Tron.Crypto;
using ChainKit.Tron.Protocol;
using ChainKit.Tron.Providers;
using ChainKit.Core.Extensions;

var provider = new TronHttpProvider(TronNetwork.Nile);
var account = TronAccount.FromPrivateKey(Convert.FromHexString("your_private_key"));

// 步驟 1：取得最新區塊以設定 ref block
var block = await provider.GetNowBlockAsync();

// 步驟 2：從區塊資訊提取 ref block 參數
var blockNumBytes = BitConverter.GetBytes(block.BlockNumber);
if (BitConverter.IsLittleEndian) Array.Reverse(blockNumBytes);
var refBlockBytes = blockNumBytes[^2..];
var refBlockHash = Convert.FromHexString(block.BlockId[..16]);

// 步驟 3：建構交易（金額單位：Sun）
var tx = new TransactionBuilder()
    .CreateTransfer(
        ownerAddress: account.HexAddress,
        toAddress: TronAddress.ToHex("TRecipientAddress..."),
        amount: 10_000_000)               // 10 TRX = 10,000,000 Sun
    .SetRefBlock(refBlockBytes, refBlockHash)
    .Build();

// 步驟 4：簽章
var signedTx = TransactionUtils.Sign(tx, account.PrivateKey);

// 步驟 5：廣播
var broadcastResult = await provider.BroadcastTransactionAsync(signedTx);

if (broadcastResult.Success)
    Console.WriteLine($"廣播成功！TxId: {broadcastResult.TxId}");
else
    Console.WriteLine($"廣播失敗: {broadcastResult.Message}");
```

### TRC20 轉帳（高階）

```csharp
// 一步完成 TRC20 轉帳
var result = await client.TransferTrc20Async(
    from: account,
    contractAddress: "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",  // USDT 合約地址
    toAddress: "TRecipientAddress...",
    amount: 100m,                                              // 100 USDT（人類可讀金額）
    decimals: 6);                                              // USDT 是 6 位小數

if (result.Success)
{
    Console.WriteLine($"TRC20 轉帳成功！TxId: {result.Data!.TxId}");
    Console.WriteLine($"金額: {result.Data.Amount}");
}
else
{
    Console.WriteLine($"TRC20 轉帳失敗: {result.Error!.Message}");
}
```

> **注意：** `decimals` 是必填參數，SDK 不會自動查詢。consumer 應透過 `Trc20Contract.DecimalsAsync()` 或 `GetBalanceAsync` 回傳的 `Trc20BalanceInfo.Decimals` 取得。

---

## TRC20 代幣操作

使用 `Trc20Contract` 進行豐富的 TRC20 合約互動。

### 查詢代幣資訊

```csharp
var trc20 = client.GetTrc20Contract(
    contractAddress: "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",  // USDT
    ownerAccount: account);

// 查詢代幣基本資訊
var nameResult = await trc20.NameAsync();           // TronResult<string>
var symbolResult = await trc20.SymbolAsync();       // TronResult<string>
var decimalsResult = await trc20.DecimalsAsync();   // TronResult<byte>
var supplyResult = await trc20.TotalSupplyAsync();  // TronResult<decimal>

if (nameResult.Success)    Console.WriteLine($"名稱:     {nameResult.Data}");
if (symbolResult.Success)  Console.WriteLine($"符號:     {symbolResult.Data}");
if (decimalsResult.Success) Console.WriteLine($"小數位:   {decimalsResult.Data}");
if (supplyResult.Success)  Console.WriteLine($"總供應量: {supplyResult.Data}");

// 查詢餘額（回傳 decimal，已根據 decimals 轉換）
var balanceResult = await trc20.BalanceOfAsync("TTargetAddress...");  // TronResult<decimal>
if (balanceResult.Success)
    Console.WriteLine($"餘額: {balanceResult.Data}");

// 查詢授權額度
var allowanceResult = await trc20.AllowanceAsync(     // TronResult<decimal>
    owner: "TOwnerAddress...",
    spender: "TSpenderAddress...");
if (allowanceResult.Success)
    Console.WriteLine($"授權額度: {allowanceResult.Data}");
```

### 轉帳

```csharp
var trc20 = client.GetTrc20Contract("TContractAddress...", account);

// 轉帳代幣（金額會自動根據 decimals 轉換）
var result = await trc20.TransferAsync(      // TronResult<TransferResult>
    to: "TRecipientAddress...",
    amount: 50.5m);                          // 人類可讀金額

if (result.Success)
    Console.WriteLine($"轉帳成功！TxId: {result.Data!.TxId}");
else
    Console.WriteLine($"轉帳失敗: {result.Error!.Message}");
```

### 授權 (Approve)

```csharp
var trc20 = client.GetTrc20Contract("TContractAddress...", account);

// 授權另一個地址使用你的代幣
var result = await trc20.ApproveAsync(       // TronResult<TransferResult>
    spender: "TSpenderAddress...",
    amount: 1000m);

if (result.Success)
    Console.WriteLine($"授權成功！TxId: {result.Data!.TxId}");
```

### Mint / Burn

```csharp
var trc20 = client.GetTrc20Contract("TYourTokenContract...", account);

// 鑄造新代幣（需要 minter 權限）
var mintResult = await trc20.MintAsync(      // TronResult<TransferResult>
    to: "TRecipientAddress...",
    amount: 10000m);

// 銷毀自己的代幣
var burnResult = await trc20.BurnAsync(amount: 500m);  // TronResult<TransferResult>

// 銷毀他人的代幣（需要 allowance）
var burnFromResult = await trc20.BurnFromAsync(        // TronResult<TransferResult>
    from: "TFromAddress...",
    amount: 200m);
```

---

## 資源管理

Tron 使用 Stake 2.0 機制，質押 TRX 可以獲取頻寬（Bandwidth）和能量（Energy）。

### 質押 TRX 獲取資源

```csharp
using ChainKit.Tron.Models;

// 質押 100 TRX 獲取能量
var result = await client.StakeTrxAsync(
    account: account,
    trxAmount: 100m,
    resource: ResourceType.Energy);

if (result.Success)
{
    var stake = result.Data!;
    Console.WriteLine($"質押成功！TxId: {stake.TxId}");
    Console.WriteLine($"質押金額: {stake.Amount} TRX");
    Console.WriteLine($"資源類型: {stake.Resource}");      // Energy
}

// 質押 50 TRX 獲取頻寬
var bwResult = await client.StakeTrxAsync(
    account, 50m, ResourceType.Bandwidth);
```

### 解除質押

```csharp
// 解除質押 50 TRX 的能量
var result = await client.UnstakeTrxAsync(
    account: account,
    trxAmount: 50m,
    resource: ResourceType.Energy);

if (result.Success)
{
    var unstake = result.Data!;
    Console.WriteLine($"解除質押成功！TxId: {unstake.TxId}");
    Console.WriteLine($"解除金額: {unstake.Amount} TRX");
}
```

### 委託資源

將你的頻寬或能量委託給其他地址使用：

```csharp
// 委託 100 TRX 的能量給另一個地址
var result = await client.DelegateResourceAsync(
    account: account,
    receiverAddress: "TReceiverAddress...",
    trxAmount: 100m,
    resource: ResourceType.Energy,
    lockPeriod: false);                    // true = 鎖定期間不可解除

if (result.Success)
{
    var d = result.Data!;
    Console.WriteLine($"委託成功！TxId: {d.TxId}");
    Console.WriteLine($"接收者: {d.ReceiverAddress}");
    Console.WriteLine($"金額:   {d.Amount} TRX");
    Console.WriteLine($"資源:   {d.Resource}");
}
```

### 解除委託

```csharp
var result = await client.UndelegateResourceAsync(
    account: account,
    receiverAddress: "TReceiverAddress...",
    trxAmount: 50m,
    resource: ResourceType.Energy);

if (result.Success)
    Console.WriteLine($"解除委託成功！TxId: {result.Data!.TxId}");
```

---

## 合約部署

### 一鍵部署 TRC20 代幣

SDK 內建 TRC20 合約範本，支援 mint 和 burn 功能：

```csharp
using System.Numerics;
using ChainKit.Tron.Models;

var options = new Trc20TokenOptions(
    Name: "My Token",
    Symbol: "MTK",
    Decimals: 18,
    InitialSupply: BigInteger.Parse("1000000000000000000000000"),  // 1,000,000 顆
    Mintable: true,       // 預設 true
    Burnable: true);      // 預設 true

var result = await client.DeployTrc20TokenAsync(account, options);

if (result.Success)
{
    Console.WriteLine($"部署成功！TxId: {result.Data!.TxId}");
    // 合約地址需要稍後透過 GetTransactionInfoByIdAsync 查詢
    Console.WriteLine("請稍後使用 GetTransactionDetailAsync 查詢合約地址");
}
```

### 部署自訂合約

```csharp
// 載入你的合約 bytecode 和 ABI
var bytecode = File.ReadAllBytes("path/to/contract.bin");
var abi = File.ReadAllText("path/to/contract.abi");

var result = await client.DeployContractAsync(
    account: account,
    bytecode: bytecode,
    abi: abi,
    feeLimit: 500_000_000);               // 500 TRX fee limit（預設 100 TRX）

if (result.Success)
{
    Console.WriteLine($"部署成功！TxId: {result.Data!.TxId}");
}
```

---

## 交易監聽

SDK 提供兩種區塊串流模式，搭配 `TronTransactionWatcher` 進行多地址交易監聽。

### Polling 模式（不需節點）

透過 HTTP API 定時輪詢新區塊，適合大多數場景：

```csharp
using ChainKit.Tron.Watching;
using ChainKit.Tron.Providers;

var provider = new TronHttpProvider(TronNetwork.Mainnet);

// 建立 Polling 串流，每 3 秒輪詢一次（預設值）
var stream = new PollingBlockStream(provider, intervalMs: 3000);

// 建立監聽器（傳入 provider 可自動解析 TRC20 代幣符號和小數位）
var watcher = new TronTransactionWatcher(stream, provider);

// 註冊要監聽的地址
watcher.WatchAddress("TYourAddress...");
watcher.WatchAddresses(new[] { "TAddress1...", "TAddress2..." });

// 訂閱事件 — 入帳
watcher.OnTrxReceived += (sender, e) =>
{
    Console.WriteLine($"收到 TRX！");
    Console.WriteLine($"  TxId: {e.TxId}");
    Console.WriteLine($"  從:   {e.FromAddress}");
    Console.WriteLine($"  到:   {e.ToAddress}");
    Console.WriteLine($"  金額: {e.Amount} TRX");
    Console.WriteLine($"  區塊: {e.BlockNumber}");
    Console.WriteLine($"  時間: {e.Timestamp}");
};

watcher.OnTrc20Received += (sender, e) =>
{
    Console.WriteLine($"收到 TRC20！");
    Console.WriteLine($"  TxId:     {e.TxId}");
    Console.WriteLine($"  合約:     {e.ContractAddress}");
    Console.WriteLine($"  符號:     {e.Symbol}");
    Console.WriteLine($"  原始金額: {e.RawAmount}");          // 最小單位，永遠有值
    Console.WriteLine($"  金額:     {e.Amount}");             // 轉換後，可能為 null
    Console.WriteLine($"  小數位:   {e.Decimals}");
};

// 訂閱事件 — 轉出
watcher.OnTrxSent += (sender, e) =>
{
    Console.WriteLine($"轉出 TRX！");
    Console.WriteLine($"  TxId: {e.TxId}");
    Console.WriteLine($"  從:   {e.FromAddress}");
    Console.WriteLine($"  到:   {e.ToAddress}");
    Console.WriteLine($"  金額: {e.Amount} TRX");
};

watcher.OnTrc20Sent += (sender, e) =>
{
    Console.WriteLine($"轉出 TRC20！");
    Console.WriteLine($"  TxId:     {e.TxId}");
    Console.WriteLine($"  合約:     {e.ContractAddress}");
    Console.WriteLine($"  符號:     {e.Symbol}");
    Console.WriteLine($"  原始金額: {e.RawAmount}");
    Console.WriteLine($"  金額:     {e.Amount}");
};

// 訂閱事件 — 交易生命週期（確認 / 失敗）
watcher.OnTransactionConfirmed += (sender, e) =>
{
    Console.WriteLine($"交易確認：{e.TxId} (區塊 {e.BlockNumber})");
};

watcher.OnTransactionFailed += (sender, e) =>
{
    Console.WriteLine($"交易失敗：{e.TxId} - {e.Reason} ({e.Message})");
};

// 啟動監聽
await watcher.StartAsync();

// ... 程式運行中 ...

// 停止監聽
await watcher.StopAsync();
```

### ZMQ 模式（需自架節點）

透過 ZeroMQ 訂閱推送的新區塊，延遲最低：

```csharp
// 連接到你自架節點的 ZMQ 端點
var stream = new ZmqBlockStream("tcp://your-node:5555");

// 傳入 provider 以支援確認追蹤和代幣符號解析
var watcher = new TronTransactionWatcher(stream, provider);

watcher.WatchAddress("TYourAddress...");
watcher.OnTrxReceived += (sender, e) => { /* ... */ };

await watcher.StartAsync();
```

### 地址匹配規則

`WatchAddress` 註冊的地址會同時匹配交易的發送方和接收方。對於 TRC20 交易，匹配邏輯如下：

- **錢包地址**（推薦）：匹配該地址作為發送方或接收方的 TRC20 轉帳
- **合約地址**：匹配該合約上的**所有** TRC20 轉帳（不限特定收款方）

如果你只需要監聽特定錢包的 TRC20 收發，應該註冊錢包地址而非合約地址。

### 動態管理監聽地址

監聽過程中可以動態增減地址，無需重啟：

```csharp
await watcher.StartAsync();

// 執行中新增地址
watcher.WatchAddress("TNewAddress...");

// 執行中移除地址
watcher.UnwatchAddress("TOldAddress...");

// 批量新增
watcher.WatchAddresses(new[] { "TAddr1...", "TAddr2...", "TAddr3..." });
```

### 交易生命週期事件

Watcher 追蹤完整的三階段交易生命週期：

| 階段 | 事件 | 觸發時機 |
|------|------|----------|
| Unconfirmed | `OnTrx/Trc20 Received/Sent` | 交易出現在 block 中 |
| Confirmed | `OnTransactionConfirmed` | Solidity Node 確認（~57 秒） |
| Failed | `OnTransactionFailed` | 合約執行失敗或確認逾時 |

TRC20 交易在 Solidity Node 確認後，確認追蹤器會檢查 `ReceiptResult`。`REVERT`、`OUT_OF_ENERGY` 等結果會觸發 `OnTransactionFailed`，而非 `OnTransactionConfirmed`。

```csharp
// 完整生命週期監聽範例
watcher.OnTrxReceived += (s, e) => Console.WriteLine($"Incoming TRX: {e.Amount} from {e.FromAddress}");
watcher.OnTrxSent += (s, e) => Console.WriteLine($"Outgoing TRX: {e.Amount} to {e.ToAddress}");
watcher.OnTrc20Received += (s, e) => Console.WriteLine($"Incoming {e.Symbol}: {e.Amount}");
watcher.OnTrc20Sent += (s, e) => Console.WriteLine($"Outgoing {e.Symbol}: {e.Amount}");
watcher.OnTransactionConfirmed += (s, e) => Console.WriteLine($"Confirmed: {e.TxId}");
watcher.OnTransactionFailed += (s, e) => Console.WriteLine($"Failed: {e.TxId} - {e.Reason}");
```

---

## 工具類

### Hex / Base58 / ABI

```csharp
using ChainKit.Core.Extensions;
using ChainKit.Tron.Crypto;

// Hex 轉換
byte[] bytes = "41abcdef".FromHex();
string hex = bytes.ToHex();                      // "41abcdef"

// Base58Check 轉換
string base58 = bytes.ToBase58Check();
byte[] decoded = base58.FromBase58Check();

// Tron 地址轉換
string hexAddr = TronAddress.ToHex("T...");      // "41..."
string b58Addr = TronAddress.ToBase58("41...");   // "T..."
bool valid = TronAddress.IsValid("T...");

// ABI 編碼/解碼
byte[] selector = AbiEncoder.EncodeFunctionSelector("transfer(address,uint256)");
BigInteger value = AbiEncoder.DecodeUint256(data);
string text = AbiEncoder.DecodeString(data);
```

---

## 資源清理

`TronClient`、`TronHttpProvider`、`TronGrpcProvider`、`Trc20Contract`、`TronAccount` 都實作 `IDisposable`；`TronTransactionWatcher` 實作 `IAsyncDisposable`。

> **`TronAccount.Dispose()` 會清零私鑰記憶體。** 在安全敏感場景中，務必 dispose 不再使用的帳戶物件。

```csharp
// 方式 1：using 語句（推薦）
using var provider = new TronHttpProvider(TronNetwork.Mainnet);
using var client = new TronClient(provider);

// 方式 2：using 區塊
using (var trc20 = client.GetTrc20Contract("TContract...", account))
{
    var name = await trc20.NameAsync();
}  // 這裡自動 dispose

// 方式 3：手動 dispose
var client = new TronClient(provider);
try
{
    // 使用 client...
}
finally
{
    client.Dispose();
}

// TronTransactionWatcher 使用 IAsyncDisposable
await using var watcher = new TronTransactionWatcher(stream, provider);
await watcher.StartAsync();
// 離開 scope 時自動呼叫 StopAsync + Dispose
```
