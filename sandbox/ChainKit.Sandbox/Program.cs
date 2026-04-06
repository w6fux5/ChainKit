using System.Threading.Channels;
using ChainKit.Tron;
using ChainKit.Tron.Contracts;
using ChainKit.Tron.Crypto;
using ChainKit.Tron.Models;
using ChainKit.Tron.Protocol;
using ChainKit.Tron.Providers;
using ChainKit.Tron.Watching;
using Microsoft.AspNetCore.Mvc;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "ChainKit Tron SDK",
        Version = "v1",
        Description = "Tron 區塊鏈 SDK 測試介面"
    });
    c.TagActionsBy(api => [api.GroupName ?? api.ActionDescriptor.EndpointMetadata
        .OfType<TagsAttribute>().FirstOrDefault()?.Tags.First() ?? "Other"]);
});
builder.Services.AddOpenApi();

// 從 appsettings.json 讀取 Tron 節點設定
var tronConfig = builder.Configuration.GetSection("Tron");
var httpEndpoint = tronConfig["HttpEndpoint"] ?? "https://nile.trongrid.io";
var httpSolidity = tronConfig["HttpSolidityEndpoint"];
var grpcFullNode = tronConfig["GrpcFullNodeEndpoint"];
var grpcSolidity = tronConfig["GrpcSolidityEndpoint"];
var apiKey = tronConfig["ApiKey"];

// Provider：優先 gRPC，fallback HTTP
if (!string.IsNullOrEmpty(grpcFullNode))
{
    builder.Services.AddSingleton<ITronProvider>(_ =>
        new TronGrpcProvider(grpcFullNode, string.IsNullOrEmpty(grpcSolidity) ? null : grpcSolidity));
}
else
{
    builder.Services.AddSingleton<ITronProvider>(_ =>
        new TronHttpProvider(httpEndpoint,
            string.IsNullOrEmpty(httpSolidity) ? null : httpSolidity,
            string.IsNullOrEmpty(apiKey) ? null : apiKey));
}
builder.Services.AddSingleton<TronClient>(sp => new TronClient(sp.GetRequiredService<ITronProvider>()));

// Watcher
var watcherConfig = builder.Configuration.GetSection("Tron:Watcher");
var pollingIntervalMs = watcherConfig.GetValue("PollingIntervalMs", 3000);
var confirmationIntervalMs = watcherConfig.GetValue("ConfirmationIntervalMs", 3000);
var maxPendingAgeMinutes = watcherConfig.GetValue("MaxPendingAgeMinutes", 5);
var zmqEndpoint = builder.Configuration["Tron:ZmqEndpoint"];

builder.Services.AddSingleton<ITronBlockStream>(sp =>
{
    if (!string.IsNullOrEmpty(zmqEndpoint))
        return new ZmqBlockStream(zmqEndpoint);
    return new PollingBlockStream(sp.GetRequiredService<ITronProvider>(), pollingIntervalMs);
});
builder.Services.AddSingleton<TronTransactionWatcher>(sp =>
    new TronTransactionWatcher(
        sp.GetRequiredService<ITronBlockStream>(),
        sp.GetRequiredService<ITronProvider>(),
        confirmationIntervalMs,
        TimeSpan.FromMinutes(maxPendingAgeMinutes)));
builder.Services.AddSingleton<WatcherEventBus>();
builder.Services.AddHostedService<WatcherBackgroundService>();

var app = builder.Build();

// Swagger UI — /swagger
app.UseSwagger();
app.UseSwaggerUI();

// Scalar — /scalar/v1
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.WithTitle("ChainKit Tron SDK Sandbox");
    options.WithTheme(ScalarTheme.BluePlanet);
});

// ============================================================
//  Wallet — 錢包建立與地址工具
// ============================================================

app.MapPost("/api/wallet/create", () =>
{
    var account = TronAccount.Create();
    return Results.Ok(new
    {
        account.Address,
        account.HexAddress,
        PublicKey = Convert.ToHexString(account.PublicKey).ToLowerInvariant(),
        PrivateKey = Convert.ToHexString(account.PrivateKey).ToLowerInvariant()
    });
})
.WithTags("Wallet")
.WithSummary("建立新錢包")
.WithDescription("隨機產生私鑰，回傳 TronAccount 所有屬性");

app.MapPost("/api/wallet/from-mnemonic", (MnemonicRequest req) =>
{
    var account = TronAccount.FromMnemonic(req.Mnemonic, req.Index);
    return Results.Ok(new
    {
        account.Address,
        account.HexAddress,
        PublicKey = Convert.ToHexString(account.PublicKey).ToLowerInvariant(),
        PrivateKey = Convert.ToHexString(account.PrivateKey).ToLowerInvariant()
    });
})
.WithTags("Wallet")
.WithSummary("從助記詞匯入錢包")
.WithDescription("BIP39 助記詞 + derivation index，回傳 TronAccount 所有屬性");

app.MapGet("/api/wallet/validate/{address}", (string address) =>
    Results.Ok(TronAddress.IsValid(address))
)
.WithTags("Wallet")
.WithSummary("驗證地址格式")
.WithDescription("回傳 bool，支援 Base58（T 開頭）和 Hex（41 開頭）格式");

app.MapGet("/api/wallet/address/to-base58/{hexAddress}", (string hexAddress) =>
{
    try { return Results.Ok(TronAddress.ToBase58(hexAddress)); }
    catch (Exception ex) { return Results.BadRequest(new { Error = ex.Message }); }
})
.WithTags("Wallet")
.WithSummary("Hex 地址轉 Base58");

app.MapGet("/api/wallet/address/to-hex/{base58Address}", (string base58Address) =>
{
    try { return Results.Ok(TronAddress.ToHex(base58Address)); }
    catch (Exception ex) { return Results.BadRequest(new { Error = ex.Message }); }
})
.WithTags("Wallet")
.WithSummary("Base58 地址轉 Hex");

app.MapPost("/api/wallet/from-private-key", (PrivateKeyRequest req) =>
{
    var account = TronAccount.FromPrivateKey(Convert.FromHexString(req.PrivateKey));
    return Results.Ok(new
    {
        account.Address,
        account.HexAddress,
        PublicKey = Convert.ToHexString(account.PublicKey).ToLowerInvariant()
    });
})
.WithTags("Wallet")
.WithSummary("從私鑰匯入錢包")
.WithDescription("回傳 TronAccount 屬性（Address、HexAddress、PublicKey）");

// ============================================================
//  Account — 帳戶查詢
// ============================================================

app.MapGet("/api/account/{address}/balance", async (string address, [FromQuery] string[]? trc20, TronClient tron) =>
{
    var result = await tron.GetBalanceAsync(address, trc20 ?? []);
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("Account")
.WithSummary("查詢 TRX 餘額 + TRC20 餘額")
.WithDescription("回傳 TRX 餘額，可選帶入 trc20 合約地址查詢 TRC20 餘額。例：?trc20=TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t");

app.MapGet("/api/account/{address}/resources", async (string address, TronClient tron) =>
{
    var result = await tron.GetResourceInfoAsync(address);
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("Account")
.WithSummary("資源詳情")
.WithDescription("Bandwidth、Energy、質押量、委託資訊");

app.MapGet("/api/account/{address}/raw", async (string address, ITronProvider provider) =>
{
    var info = await provider.GetAccountAsync(address);
    return Results.Ok(info);
})
.WithTags("Account")
.WithSummary("原始帳戶資料（低階）")
.WithDescription("直接查詢 Full Node /wallet/getaccount，回傳未加工的 AccountInfo");

app.MapGet("/api/account/{address}/raw-resource", async (string address, ITronProvider provider) =>
{
    var info = await provider.GetAccountResourceAsync(address);
    return Results.Ok(info);
})
.WithTags("Account")
.WithSummary("原始資源資料（低階）")
.WithDescription("直接查詢 Full Node /wallet/getaccountresource");

app.MapGet("/api/account/{address}/transactions", async (
    string address, int? limit, ITronProvider provider) =>
{
    var txs = await provider.GetAccountTransactionsAsync(address, limit ?? 10);
    return Results.Ok(txs);
})
.WithTags("Account")
.WithSummary("帳戶交易紀錄（低階）")
.WithDescription("透過 TronGrid v1 API 查詢，可指定 limit（預設 10）");

// ============================================================
//  Transfer — TRX 轉帳
// ============================================================

app.MapPost("/api/transfer/trx", async (TrxTransferRequest req, TronClient tron) =>
{
    var account = TronAccount.FromPrivateKey(Convert.FromHexString(req.PrivateKey));
    var result = await tron.TransferTrxAsync(account, req.ToAddress, req.Amount);
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("Transfer")
.WithSummary("TRX 轉帳")
.WithDescription("高階 API：建立交易 → 簽名 → 廣播，Amount 單位為 TRX");

// ============================================================
//  TRC20 — 代幣操作
// ============================================================

app.MapPost("/api/trc20/transfer", async (Trc20TransferRequest req, TronClient tron) =>
{
    var account = TronAccount.FromPrivateKey(Convert.FromHexString(req.PrivateKey));
    var result = await tron.TransferTrc20Async(
        account, req.ContractAddress, req.ToAddress, req.Amount, req.Decimals);
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("TRC20")
.WithSummary("TRC20 轉帳（高階）")
.WithDescription("高階 API：ABI 編碼 → TriggerSmartContract → 簽名 → 廣播");

app.MapGet("/api/trc20/{contractAddress}/name", async (
    string contractAddress, ITronProvider provider) =>
{
    using var contract = new Trc20Contract(provider, contractAddress, TronAccount.Create());
    var result = await contract.NameAsync();
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("TRC20")
.WithSummary("代幣名稱（低階）")
.WithDescription("呼叫合約 name() 方法");

app.MapGet("/api/trc20/{contractAddress}/symbol", async (
    string contractAddress, ITronProvider provider) =>
{
    using var contract = new Trc20Contract(provider, contractAddress, TronAccount.Create());
    var result = await contract.SymbolAsync();
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("TRC20")
.WithSummary("代幣符號（低階）")
.WithDescription("呼叫合約 symbol() 方法");

app.MapGet("/api/trc20/{contractAddress}/decimals", async (
    string contractAddress, ITronProvider provider) =>
{
    using var contract = new Trc20Contract(provider, contractAddress, TronAccount.Create());
    var result = await contract.DecimalsAsync();
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("TRC20")
.WithSummary("代幣小數位（低階）")
.WithDescription("呼叫合約 decimals() 方法");

app.MapGet("/api/trc20/{contractAddress}/total-supply", async (
    string contractAddress, ITronProvider provider) =>
{
    using var contract = new Trc20Contract(provider, contractAddress, TronAccount.Create());
    var result = await contract.TotalSupplyAsync();
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("TRC20")
.WithSummary("代幣總供應量（低階）")
.WithDescription("呼叫合約 totalSupply() 方法");

app.MapGet("/api/trc20/{contractAddress}/balance-of/{ownerAddress}", async (
    string contractAddress, string ownerAddress, ITronProvider provider) =>
{
    using var contract = new Trc20Contract(provider, contractAddress, TronAccount.Create());
    var result = await contract.BalanceOfAsync(ownerAddress);
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("TRC20")
.WithSummary("代幣餘額（低階）")
.WithDescription("呼叫合約 balanceOf(address) 方法");

app.MapGet("/api/trc20/{contractAddress}/allowance/{owner}/{spender}", async (
    string contractAddress, string owner, string spender, ITronProvider provider) =>
{
    using var contract = new Trc20Contract(provider, contractAddress, TronAccount.Create());
    var result = await contract.AllowanceAsync(owner, spender);
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("TRC20")
.WithSummary("授權額度（低階）")
.WithDescription("呼叫合約 allowance(owner, spender) 方法");

app.MapPost("/api/trc20/contract-transfer", async (Trc20ContractTransferRequest req, ITronProvider provider) =>
{
    var account = TronAccount.FromPrivateKey(Convert.FromHexString(req.PrivateKey));
    using var contract = new Trc20Contract(provider, req.ContractAddress, account);
    var result = await contract.TransferAsync(req.ToAddress, req.Amount);
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("TRC20")
.WithSummary("TRC20 轉帳（低階）")
.WithDescription("透過 Trc20Contract 直接呼叫 transfer(to, amount)");

app.MapPost("/api/trc20/approve", async (Trc20ApproveRequest req, ITronProvider provider) =>
{
    var account = TronAccount.FromPrivateKey(Convert.FromHexString(req.PrivateKey));
    using var contract = new Trc20Contract(provider, req.ContractAddress, account);
    var result = await contract.ApproveAsync(req.SpenderAddress, req.Amount);
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("TRC20")
.WithSummary("授權（低階）")
.WithDescription("呼叫合約 approve(spender, amount)");

app.MapPost("/api/trc20/mint", async (Trc20MintRequest req, ITronProvider provider) =>
{
    var account = TronAccount.FromPrivateKey(Convert.FromHexString(req.PrivateKey));
    using var contract = new Trc20Contract(provider, req.ContractAddress, account);
    var result = await contract.MintAsync(req.ToAddress, req.Amount);
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("TRC20")
.WithSummary("鑄幣（低階）")
.WithDescription("呼叫合約 mint(to, amount)，需要 minter 權限");

app.MapPost("/api/trc20/burn", async (Trc20BurnRequest req, ITronProvider provider) =>
{
    var account = TronAccount.FromPrivateKey(Convert.FromHexString(req.PrivateKey));
    using var contract = new Trc20Contract(provider, req.ContractAddress, account);
    var result = await contract.BurnAsync(req.Amount);
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("TRC20")
.WithSummary("銷毀（低階）")
.WithDescription("呼叫合約 burn(amount)");

app.MapPost("/api/trc20/burn-from", async (Trc20BurnFromRequest req, ITronProvider provider) =>
{
    var account = TronAccount.FromPrivateKey(Convert.FromHexString(req.PrivateKey));
    using var contract = new Trc20Contract(provider, req.ContractAddress, account);
    var result = await contract.BurnFromAsync(req.FromAddress, req.Amount);
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("TRC20")
.WithSummary("從指定地址銷毀（低階）")
.WithDescription("呼叫合約 burnFrom(from, amount)，需要事先 approve");

// ============================================================
//  Staking — 質押與資源委託（Stake 2.0）
// ============================================================

app.MapPost("/api/staking/stake", async (StakeRequest req, TronClient tron) =>
{
    var account = TronAccount.FromPrivateKey(Convert.FromHexString(req.PrivateKey));
    var result = await tron.StakeTrxAsync(account, req.TrxAmount, req.Resource);
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("Staking")
.WithSummary("質押 TRX")
.WithDescription("Freeze TRX 以獲得 Bandwidth 或 Energy 資源");

app.MapPost("/api/staking/unstake", async (StakeRequest req, TronClient tron) =>
{
    var account = TronAccount.FromPrivateKey(Convert.FromHexString(req.PrivateKey));
    var result = await tron.UnstakeTrxAsync(account, req.TrxAmount, req.Resource);
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("Staking")
.WithSummary("解除質押 TRX")
.WithDescription("Unfreeze TRX，解除後需等待 14 天提領");

app.MapPost("/api/staking/delegate", async (DelegateRequest req, TronClient tron) =>
{
    var account = TronAccount.FromPrivateKey(Convert.FromHexString(req.PrivateKey));
    var result = await tron.DelegateResourceAsync(
        account, req.ReceiverAddress, req.TrxAmount, req.Resource, req.LockPeriod);
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("Staking")
.WithSummary("委託資源")
.WithDescription("將質押的 Bandwidth/Energy 委託給其他地址使用");

app.MapPost("/api/staking/undelegate", async (UndelegateRequest req, TronClient tron) =>
{
    var account = TronAccount.FromPrivateKey(Convert.FromHexString(req.PrivateKey));
    var result = await tron.UndelegateResourceAsync(
        account, req.ReceiverAddress, req.TrxAmount, req.Resource);
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("Staking")
.WithSummary("取消委託")
.WithDescription("收回委託給其他地址的資源");

app.MapGet("/api/staking/delegation/{address}", async (string address, ITronProvider provider) =>
{
    var index = await provider.GetDelegatedResourceAccountIndexAsync(address);
    return Results.Ok(index);
})
.WithTags("Staking")
.WithSummary("委託索引（低階）")
.WithDescription("查詢地址的委託對象清單");

app.MapGet("/api/staking/delegation/{fromAddress}/to/{toAddress}", async (
    string fromAddress, string toAddress, ITronProvider provider) =>
{
    var resources = await provider.GetDelegatedResourceAsync(fromAddress, toAddress);
    return Results.Ok(resources);
})
.WithTags("Staking")
.WithSummary("委託詳情（低階）")
.WithDescription("查詢兩個地址間的委託資源量");

// ============================================================
//  Contract — 智能合約
// ============================================================

app.MapPost("/api/contract/deploy", async (DeployContractRequest req, TronClient tron) =>
{
    var account = TronAccount.FromPrivateKey(Convert.FromHexString(req.PrivateKey));
    var bytecode = Convert.FromHexString(req.Bytecode);
    var result = await tron.DeployContractAsync(account, bytecode, req.Abi, req.FeeLimit);
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("Contract")
.WithSummary("部署合約（通用）")
.WithDescription("部署任意智能合約，需提供 bytecode 和 ABI");

app.MapPost("/api/contract/deploy-trc20", async (DeployTrc20Request req, TronClient tron) =>
{
    var account = TronAccount.FromPrivateKey(Convert.FromHexString(req.PrivateKey));
    var options = new Trc20TokenOptions(req.Name, req.Symbol, req.Decimals, req.InitialSupply, req.Mintable, req.Burnable);
    var result = await tron.DeployTrc20TokenAsync(account, options);
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("Contract")
.WithSummary("部署 TRC20 代幣")
.WithDescription("使用內建模板部署標準 TRC20 代幣合約");

app.MapPost("/api/contract/call", async (ContractCallRequest req, ITronProvider provider) =>
{
    try
    {
        var param = string.IsNullOrEmpty(req.Parameter)
            ? Array.Empty<byte>()
            : Convert.FromHexString(req.Parameter);
        var result = await provider.TriggerConstantContractAsync(
            req.OwnerAddress, req.ContractAddress, req.FunctionSelector, param);
        return Results.Ok(Convert.ToHexString(result).ToLowerInvariant());
    }
    catch (Exception ex) { return Results.BadRequest(new { Error = ex.Message }); }
})
.WithTags("Contract")
.WithSummary("唯讀合約呼叫（低階）")
.WithDescription("TriggerConstantContract — 不上鏈，不消耗資源。Parameter 為 ABI 編碼的 hex");

app.MapPost("/api/contract/estimate-energy", async (ContractCallRequest req, ITronProvider provider) =>
{
    try
    {
        var param = string.IsNullOrEmpty(req.Parameter)
            ? Array.Empty<byte>()
            : Convert.FromHexString(req.Parameter);
        var energy = await provider.EstimateEnergyAsync(
            req.OwnerAddress, req.ContractAddress, req.FunctionSelector, param);
        return Results.Ok(energy);
    }
    catch (Exception ex) { return Results.BadRequest(new { Error = ex.Message }); }
})
.WithTags("Contract")
.WithSummary("估算 Energy 消耗（低階）")
.WithDescription("預估合約呼叫所需的 Energy 量");

// ============================================================
//  Transaction — 交易查詢
// ============================================================

app.MapGet("/api/transaction/{txId}", async (string txId, TronClient tron) =>
{
    var result = await tron.GetTransactionDetailAsync(txId);
    return result.Success ? Results.Ok(result.Data) : Results.BadRequest(result.Error);
})
.WithTags("Transaction")
.WithSummary("交易詳情（高階）")
.WithDescription("合併 Full Node + Solidity Node 資料，包含狀態、金額、資源消耗");

app.MapGet("/api/transaction/{txId}/raw", async (string txId, ITronProvider provider) =>
{
    var tx = await provider.GetTransactionByIdAsync(txId);
    return Results.Ok(tx);
})
.WithTags("Transaction")
.WithSummary("Full Node 交易資料（低階）")
.WithDescription("直接查詢 /wallet/gettransactionbyid");

app.MapGet("/api/transaction/{txId}/info", async (string txId, ITronProvider provider) =>
{
    var info = await provider.GetTransactionInfoByIdAsync(txId);
    return Results.Ok(info);
})
.WithTags("Transaction")
.WithSummary("Solidity Node 交易資料（低階）")
.WithDescription("直接查詢 /walletsolidity/gettransactioninfobyid，含 receipt 和確認狀態");

// ============================================================
//  Block — 區塊查詢
// ============================================================

app.MapGet("/api/block/latest", async (ITronProvider provider) =>
{
    var block = await provider.GetNowBlockAsync();
    return Results.Ok(block);
})
.WithTags("Block")
.WithSummary("最新區塊")
.WithDescription("查詢 Full Node 當前最新區塊");

app.MapGet("/api/block/{num}", async (long num, ITronProvider provider) =>
{
    var block = await provider.GetBlockByNumAsync(num);
    return Results.Ok(block);
})
.WithTags("Block")
.WithSummary("依區塊號查詢")
.WithDescription("查詢指定區塊號的區塊資料");

// ============================================================
//  Watcher — 交易監聽
// ============================================================

app.MapPost("/api/watcher/watch/{address}", (string address, TronTransactionWatcher watcher) =>
{
    watcher.WatchAddress(address);
    return Results.Ok(new { Message = $"Watching {address}" });
})
.WithTags("Watcher")
.WithSummary("新增監聽地址")
.WithDescription("開始監聽該地址的所有入帳和出帳交易");

app.MapPost("/api/watcher/watch", (WatchAddressesRequest req, TronTransactionWatcher watcher) =>
{
    watcher.WatchAddresses(req.Addresses);
    return Results.Ok(new { Message = $"Watching {req.Addresses.Length} addresses" });
})
.WithTags("Watcher")
.WithSummary("批量新增監聽地址");

app.MapDelete("/api/watcher/watch/{address}", (string address, TronTransactionWatcher watcher) =>
{
    watcher.UnwatchAddress(address);
    return Results.Ok(new { Message = $"Unwatched {address}" });
})
.WithTags("Watcher")
.WithSummary("移除監聽地址");

app.MapGet("/api/watcher/events", async (WatcherEventBus bus, HttpContext ctx) =>
{
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";

    var ch = bus.Subscribe();
    try
    {
        await foreach (var evt in ch.Reader.ReadAllAsync(ctx.RequestAborted))
        {
            await ctx.Response.WriteAsync($"data: {evt}\n\n", ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        }
    }
    finally
    {
        bus.Unsubscribe(ch);
    }
})
.WithTags("Watcher")
.WithSummary("事件串流（SSE）")
.WithDescription("Server-Sent Events 即時推送所有 watcher 事件（TRX/TRC20 收發、確認、失敗）。用瀏覽器或 curl 連線即可接收");

app.Run();

// ============================================================
//  Request DTOs
// ============================================================

record TrxTransferRequest(string PrivateKey, string ToAddress, decimal Amount);
record Trc20TransferRequest(string PrivateKey, string ContractAddress, string ToAddress, decimal Amount, int Decimals);
record StakeRequest(string PrivateKey, decimal TrxAmount, ResourceType Resource);
record DelegateRequest(string PrivateKey, string ReceiverAddress, decimal TrxAmount, ResourceType Resource, bool LockPeriod = false);
record UndelegateRequest(string PrivateKey, string ReceiverAddress, decimal TrxAmount, ResourceType Resource);
record DeployTrc20Request(string PrivateKey, string Name, string Symbol, byte Decimals, long InitialSupply, bool Mintable = true, bool Burnable = true);
record MnemonicRequest(string Mnemonic, int Index = 0);
record ContractCallRequest(string OwnerAddress, string ContractAddress, string FunctionSelector, string? Parameter = null);
record Trc20ContractTransferRequest(string PrivateKey, string ContractAddress, string ToAddress, decimal Amount);
record Trc20ApproveRequest(string PrivateKey, string ContractAddress, string SpenderAddress, decimal Amount);
record Trc20MintRequest(string PrivateKey, string ContractAddress, string ToAddress, decimal Amount);
record Trc20BurnRequest(string PrivateKey, string ContractAddress, decimal Amount);
record Trc20BurnFromRequest(string PrivateKey, string ContractAddress, string FromAddress, decimal Amount);
record PrivateKeyRequest(string PrivateKey);
record DeployContractRequest(string PrivateKey, string Bytecode, string Abi, long FeeLimit = 100_000_000);
record WatchAddressesRequest(string[] Addresses);

// ============================================================
//  Watcher Background Service + Event Bus
// ============================================================

/// <summary>
/// 廣播 watcher 事件給所有 SSE 連線。
/// </summary>
class WatcherEventBus
{
    private readonly object _lock = new();
    private readonly List<Channel<string>> _subscribers = [];

    public void Publish(string json)
    {
        lock (_lock)
        {
            foreach (var ch in _subscribers)
                ch.Writer.TryWrite(json);
        }
    }

    public Channel<string> Subscribe()
    {
        var ch = Channel.CreateBounded<string>(
            new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });
        lock (_lock) { _subscribers.Add(ch); }
        return ch;
    }

    public void Unsubscribe(Channel<string> ch)
    {
        lock (_lock) { _subscribers.Remove(ch); }
    }
}

/// <summary>
/// 啟動 TronTransactionWatcher 並將事件轉發到 EventBus。
/// </summary>
class WatcherBackgroundService(
    TronTransactionWatcher watcher,
    WatcherEventBus bus,
    ILogger<WatcherBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        watcher.OnTrxReceived += (_, e) =>
        {
            logger.LogInformation("TRX Received: {TxId} {Amount} TRX from {From}", e.TxId, e.Amount, e.FromAddress);
            bus.Publish(System.Text.Json.JsonSerializer.Serialize(new { Event = "TrxReceived", e.TxId, e.FromAddress, e.ToAddress, e.Amount, e.BlockNumber }));
        };

        watcher.OnTrxSent += (_, e) =>
        {
            logger.LogInformation("TRX Sent: {TxId} {Amount} TRX to {To}", e.TxId, e.Amount, e.ToAddress);
            bus.Publish(System.Text.Json.JsonSerializer.Serialize(new { Event = "TrxSent", e.TxId, e.FromAddress, e.ToAddress, e.Amount, e.BlockNumber }));
        };

        watcher.OnTrc20Received += (_, e) =>
        {
            logger.LogInformation("TRC20 Received: {TxId} {Amount} {Symbol} from {From}", e.TxId, e.RawAmount, e.Symbol, e.FromAddress);
            bus.Publish(System.Text.Json.JsonSerializer.Serialize(new { Event = "Trc20Received", e.TxId, e.FromAddress, e.ToAddress, e.ContractAddress, e.Symbol, e.RawAmount, e.Amount, e.BlockNumber }));
        };

        watcher.OnTrc20Sent += (_, e) =>
        {
            logger.LogInformation("TRC20 Sent: {TxId} {Amount} {Symbol} to {To}", e.TxId, e.RawAmount, e.Symbol, e.ToAddress);
            bus.Publish(System.Text.Json.JsonSerializer.Serialize(new { Event = "Trc20Sent", e.TxId, e.FromAddress, e.ToAddress, e.ContractAddress, e.Symbol, e.RawAmount, e.Amount, e.BlockNumber }));
        };

        watcher.OnTransactionConfirmed += (_, e) =>
        {
            logger.LogInformation("Confirmed: {TxId} at block {Block}", e.TxId, e.BlockNumber);
            bus.Publish(System.Text.Json.JsonSerializer.Serialize(new { Event = "Confirmed", e.TxId, e.BlockNumber }));
        };

        watcher.OnTransactionFailed += (_, e) =>
        {
            logger.LogWarning("Failed: {TxId} reason={Reason} {Message}", e.TxId, e.Reason, e.Message);
            bus.Publish(System.Text.Json.JsonSerializer.Serialize(new { Event = "Failed", e.TxId, Reason = e.Reason.ToString(), e.Message, e.BlockNumber }));
        };

        logger.LogInformation("Watcher started");
        await watcher.StartAsync(stoppingToken);

        // Keep running until stopped
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }

        await watcher.StopAsync();
        logger.LogInformation("Watcher stopped");
    }
}
