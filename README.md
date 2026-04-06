# ChainKit

Multi-chain blockchain SDK for .NET. Currently supports **Tron**.

## Installation

```bash
dotnet add package W6fux5.ChainKit.Tron
```

`W6fux5.ChainKit.Core` is included as a dependency automatically.

## Quick Start

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

// Query balance
var balance = await client.GetBalanceAsync(account.Address);
Console.WriteLine($"TRX: {balance.Data!.TrxBalance}");
```

## Features

### High-Level API (TronClient)

- **Transfer**: TRX transfers with automatic ref block handling
- **Balance**: TRX + TRC20 token balances in one call
- **Staking**: Stake/unstake TRX for Energy/Bandwidth (Stake 2.0)
- **Delegation**: Delegate/undelegate resources to other addresses
- **Resource Rate**: Bidirectional TRX <-> Energy/Bandwidth exchange rate estimation
- **Transaction Detail**: Merged Full Node + Solidity Node transaction info with status tracking

### TRC20 Contracts (Trc20Contract)

- **Token Info**: Name, symbol, decimals, total supply, deployer address (parallel queries)
- **Transfer / Approve / Allowance**: Standard ERC20-compatible operations
- **Mint / Burn**: Extended operations for mintable/burnable tokens
- **Deploy**: Deploy custom TRC20 tokens from built-in template

### Transaction Watching (TronTransactionWatcher)

- **Real-time monitoring**: Watch addresses for incoming/outgoing TRX and TRC20 transfers
- **Confirmation tracking**: Automatic Solidity Node confirmation with failure detection
- **Two stream sources**: PollingBlockStream (HTTP) or ZmqBlockStream (self-hosted node)

### Providers

- **TronHttpProvider**: HTTP/HTTPS with dual endpoint support (Full Node + Solidity Node)
- **TronGrpcProvider**: gRPC for high-performance scenarios
- **Optional ILogger**: All classes accept optional `ILogger<T>` for diagnostics

## Dual Endpoint Setup

For accurate transaction confirmation, configure separate Full Node and Solidity Node endpoints:

```csharp
var provider = new TronHttpProvider(
    baseUrl: "http://your-fullnode:8090",
    solidityUrl: "http://your-soliditynode:8091");
```

## TRC20 Example

```csharp
using var contract = client.GetTrc20Contract("TContractAddress...", account);

// Get token info
var info = await contract.GetTokenInfoAsync();
Console.WriteLine($"{info.Data!.Symbol} - Decimals: {info.Data.Decimals}");

// Transfer tokens
var tx = await contract.TransferAsync("TReceiverAddress...", 100m);
```

## Resource Exchange Rate

```csharp
var rate = await client.GetResourceExchangeRateAsync(ResourceType.Energy);
Console.WriteLine($"1 TRX = {rate.Data!.ResourcePerTrx:F2} Energy");
Console.WriteLine($"100 TRX = {rate.Data.EstimateResource(100m):F0} Energy");
Console.WriteLine($"10000 Energy = {rate.Data.EstimateTrx(10000):F2} TRX");
```

## Requirements

- .NET 10.0+

## License

[Apache License 2.0](LICENSE)
