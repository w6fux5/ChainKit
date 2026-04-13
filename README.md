# ChainKit

Multi-chain blockchain SDK for .NET. Currently supports **Tron** and **EVM** (Ethereum, Polygon, and all EVM-compatible chains).

## Installation

```bash
# Tron
dotnet add package W6fux5.ChainKit.Tron

# EVM (Ethereum, Polygon, etc.)
dotnet add package W6fux5.ChainKit.Evm
```

`W6fux5.ChainKit.Core` is included as a dependency automatically.

---

## EVM (Ethereum / Polygon)

### Quick Start

```csharp
using ChainKit.Evm;
using ChainKit.Evm.Crypto;
using ChainKit.Evm.Providers;

// Connect to network
using var provider = new EvmHttpProvider(EvmNetwork.Sepolia);
using var client = new EvmClient(provider, EvmNetwork.Sepolia);

// Create or import account
var account = EvmAccount.FromPrivateKey(Convert.FromHexString("your_private_key_hex"));
// or: var account = EvmAccount.FromMnemonic("your twelve word mnemonic ...", index: 0);

// Transfer ETH
var result = await client.TransferAsync(account, "0xReceiverAddress...", 0.1m);
if (result.Success)
    Console.WriteLine($"TxHash: {result.Data!.TxId}");

// Query balance
var balance = await client.GetBalanceAsync(account.Address);
Console.WriteLine($"ETH: {balance.Data!.Balance}");  // decimal ETH
Console.WriteLine($"Wei: {balance.Data.RawBalance}"); // BigInteger Wei
```

### ERC20 Tokens

```csharp
using var contract = client.GetErc20Contract("0xContractAddress...");

// Get token info (parallel queries)
var info = await contract.GetTokenInfoAsync();
Console.WriteLine($"{info.Data!.Symbol} - Decimals: {info.Data.Decimals}");

// Query balance
var balance = await contract.BalanceOfAsync("0xHolderAddress...");

// Transfer tokens (rawAmount in smallest unit)
var tx = await contract.TransferAsync(account, "0xReceiverAddress...", rawAmount: 1_000_000);

// Approve spender
await contract.ApproveAsync(account, "0xSpenderAddress...", rawAmount: 1_000_000);
```

### Transaction Watching

```csharp
using ChainKit.Evm.Watching;

// Choose block stream: Polling (HTTP) or WebSocket
var stream = new PollingBlockStream(provider, pollInterval: TimeSpan.FromSeconds(3));
// or: var stream = new WebSocketBlockStream("wss://...", provider);

await using var watcher = new EvmTransactionWatcher(stream, provider, EvmNetwork.Sepolia);

watcher.OnNativeReceived += (_, e) =>
    Console.WriteLine($"Received {e.Amount} ETH from {e.FromAddress}");

watcher.OnErc20Received += (_, e) =>
    Console.WriteLine($"Received {e.RawAmount} {e.Symbol} from {e.FromAddress}");

watcher.OnTransactionConfirmed += (_, e) =>
    Console.WriteLine($"Confirmed: {e.TxId} at block {e.BlockNumber}");

watcher.WatchAddress("0xYourAddress...");
await watcher.StartAsync(startBlock: await provider.GetBlockNumberAsync());
```

Six events: `OnNativeReceived`, `OnNativeSent`, `OnErc20Received`, `OnErc20Sent`, `OnTransactionConfirmed`, `OnTransactionFailed`

### Supported Networks

| Network | Config | Chain ID |
|---------|--------|----------|
| Ethereum Mainnet | `EvmNetwork.EthereumMainnet` | 1 |
| Sepolia (testnet) | `EvmNetwork.Sepolia` | 11155111 |
| Polygon | `EvmNetwork.PolygonMainnet` | 137 |
| Polygon Amoy (testnet) | `EvmNetwork.PolygonAmoy` | 80002 |
| Custom | `EvmNetwork.Custom(rpcUrl, chainId, name, currency)` | any |

### Features

- **EvmClient**: Transfer ETH/POL, query balance, transaction details, block number
- **Erc20Contract**: name, symbol, decimals, totalSupply, balanceOf, allowance, transfer, approve
- **EvmTransactionWatcher**: Real-time monitoring with ERC20 detection via receipt logs
- **PollingBlockStream**: HTTP-based block polling
- **WebSocketBlockStream**: `eth_subscribe("newHeads")` with auto-reconnect and gap recovery
- **EvmAccount**: Key management, BIP-44 (`m/44'/60'`), EIP-55 checksum addresses
- **EvmSigner**: EIP-1559 (Type 2) and Legacy (EIP-155) transaction signing
- **RlpEncoder**: Self-implemented RLP encoding (no Nethereum dependency)

---

## Tron

### Quick Start

```csharp
using ChainKit.Tron;
using ChainKit.Tron.Crypto;
using ChainKit.Tron.Providers;

// Connect to Tron network
using var provider = new TronHttpProvider(TronNetwork.Nile);  // Testnet
using var client = new TronClient(provider);

// Create or import account
var account = TronAccount.FromPrivateKey(Convert.FromHexString("your_private_key_hex"));

// Transfer TRX
var result = await client.TransferTrxAsync(account, "TReceiverAddress...", 10m);
if (result.Success)
    Console.WriteLine($"TxId: {result.Data!.TxId}");

// Query balance (with optional TRC20 contracts)
var balance = await client.GetBalanceAsync(account.Address, "TUsdtContractAddress...");
Console.WriteLine($"TRX: {balance.Data!.TrxBalance}");
```

### TRC20 Tokens

```csharp
using var contract = client.GetTrc20Contract("TContractAddress...", account);

// Get token info (5 parallel queries)
var info = await contract.GetTokenInfoAsync();
Console.WriteLine($"{info.Data!.Symbol} - Decimals: {info.Data.Decimals}");

// Transfer tokens
var tx = await contract.TransferAsync("TReceiverAddress...", 100m);

// Approve / Mint / Burn
await contract.ApproveAsync("TSpenderAddress...", 1000m);
await contract.MintAsync("TReceiverAddress...", 500m);
await contract.BurnAsync(200m);
```

### Transaction Watching

```csharp
using ChainKit.Tron.Watching;

var stream = new PollingBlockStream(provider);
await using var watcher = new TronTransactionWatcher(stream, provider);

watcher.OnTrxReceived += (_, e) =>
    Console.WriteLine($"Received {e.Amount} TRX from {e.FromAddress}");

watcher.OnTrc20Received += (_, e) =>
    Console.WriteLine($"Received {e.Amount} {e.Symbol} from {e.FromAddress}");

watcher.WatchAddress("TYourAddress...");
await watcher.StartAsync();
```

Six events: `OnTrxReceived`, `OnTrxSent`, `OnTrc20Received`, `OnTrc20Sent`, `OnTransactionConfirmed`, `OnTransactionFailed`

### Features

- **TronClient**: TRX transfer, balance, staking (Stake 2.0), resource delegation, exchange rate estimation, contract deployment
- **Trc20Contract**: name, symbol, decimals, totalSupply, balanceOf, allowance, transfer, approve, mint, burn
- **TronTransactionWatcher**: Real-time monitoring with Solidity Node confirmation
- **PollingBlockStream** / **ZmqBlockStream**: HTTP polling or ZMQ push (self-hosted node)
- **TronHttpProvider**: Dual endpoint (Full Node + Solidity Node) for accurate confirmation
- **TronGrpcProvider**: gRPC for high-performance scenarios

### Dual Endpoint Setup

For accurate transaction confirmation, configure separate Full Node and Solidity Node endpoints:

```csharp
var provider = new TronHttpProvider(
    baseUrl: "http://your-fullnode:8090",
    solidityUrl: "http://your-soliditynode:8091");
```

### Supported Networks

| Network | Config |
|---------|--------|
| Mainnet | `TronNetwork.Mainnet` |
| Nile (testnet) | `TronNetwork.Nile` |
| Shasta (testnet) | `TronNetwork.Shasta` |

---

## Architecture

```
ChainKit.Core       Shared: ChainResult, IAccount, ITransaction, Keccak256, AbiEncoder, Mnemonic, TokenConverter
ChainKit.Tron       Tron-specific: Protobuf, TronClient, Trc20Contract, TronTransactionWatcher
ChainKit.Evm        EVM-specific: RLP, EvmClient, Erc20Contract, EvmTransactionWatcher
```

### Design Principles

- **Result Pattern**: Business errors return `TronResult<T>` / `EvmResult<T>` — no exceptions thrown for expected failures
- **Per-chain packages**: Each chain is a self-contained package, sharing only `ChainKit.Core`
- **Optional logging**: All public classes accept `ILogger<T>?` (defaults to `NullLogger`)
- **Thread-safe caching**: `TokenInfoCache` with `ConcurrentDictionary`, `Erc20Contract` / `Trc20Contract` with `SemaphoreSlim`
- **No heavy dependencies**: No Nethereum, no BouncyCastle — uses `NBitcoin.Secp256k1` for fast ECDSA

## Requirements

- .NET 10.0+

## License

[Apache License 2.0](LICENSE)
