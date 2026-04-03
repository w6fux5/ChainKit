# Plan 2: Providers + TronClient + Contracts Implementation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the online layer — Provider abstraction (HTTP/gRPC), high-level TronClient facade, TRC20 contract management, and all Models/DTOs.

**Architecture:** Single `ChainKit.Tron` project with `Providers/`, `Models/`, `Contracts/` namespaces. Provider layer wraps Tron node APIs. TronClient integrates multiple Provider calls into single high-level operations. All high-level APIs return `TronResult<T>`.

**Tech Stack:** .NET 10, C#, HttpClient, Grpc.Net.Client 2.76.0, xUnit, Moq/NSubstitute for mocking

**Spec:** `docs/superpowers/specs/2026-04-03-tron-sdk-design.md`

**Depends on:** Plan 1 (Core + Crypto + Protocol) — completed.

---

### Task 1: Models — TronResult + Enums + DTOs

**Files:**
- Create: `src/ChainKit.Tron/Models/TronResult.cs`
- Create: `src/ChainKit.Tron/Models/TronErrorCode.cs`
- Create: `src/ChainKit.Tron/Models/TransactionModels.cs`
- Create: `src/ChainKit.Tron/Models/AccountModels.cs`
- Create: `src/ChainKit.Tron/Models/ResourceModels.cs`
- Create: `tests/ChainKit.Tron.Tests/Models/TronResultTests.cs`

- [ ] **Step 1: Write failing tests for TronResult**

```csharp
using ChainKit.Tron.Models;
using Xunit;

namespace ChainKit.Tron.Tests.Models;

public class TronResultTests
{
    [Fact]
    public void Ok_SetsSuccessAndData()
    {
        var result = TronResult<string>.Ok("tx123");
        Assert.True(result.Success);
        Assert.Equal("tx123", result.Data);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Fail_SetsErrorCode()
    {
        var result = TronResult<string>.Fail(TronErrorCode.InsufficientBalance, "not enough TRX");
        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.Equal("InsufficientBalance", result.Error!.Code);
        Assert.Equal("not enough TRX", result.Error.Message);
    }

    [Fact]
    public void Fail_WithNodeMessage_PreservesIt()
    {
        var result = TronResult<int>.Fail(TronErrorCode.ContractExecutionFailed, "failed", "CONTRACT_EXE_ERROR");
        Assert.Equal("CONTRACT_EXE_ERROR", result.Error!.RawMessage);
    }
}
```

- [ ] **Step 2: Implement TronResult, TronErrorCode, and all DTOs**

`TronResult<T>` inherits from `ChainResult<T>` (from ChainKit.Core). `TronErrorCode` is an enum. All DTO records are defined per spec (see spec file for complete definitions):

- TransactionModels: `TransferResult`, `TronTransactionDetail`, `TransactionStatus`, `TransactionType`, `TokenTransferInfo`, `ResourceCost`, `FailureInfo`, `FailureReason`
- AccountModels: `BalanceInfo`, `AccountOverview`, `AccountInfo`, `BlockInfo`, `BroadcastResult`, `TransactionInfo`, `AccountResourceInfo`
- ResourceModels: `ResourceType`, `ResourceInfo`, `DelegationInfo`, `StakeResult`, `UnstakeResult`, `DelegateResult`, `UndelegateResult`, `DeployResult`, `Trc20TokenOptions`

- [ ] **Step 3: Run tests, verify pass**
- [ ] **Step 4: Commit** "feat(tron): add TronResult, error codes, and all DTO models"

---

### Task 2: TronNetwork Config + ITronProvider Interface

**Files:**
- Create: `src/ChainKit.Tron/Providers/TronNetwork.cs`
- Create: `src/ChainKit.Tron/Providers/ITronProvider.cs`

- [ ] **Step 1: Implement TronNetwork**

```csharp
namespace ChainKit.Tron.Providers;

public record TronNetworkConfig(
    string HttpEndpoint,
    string GrpcFullNodeEndpoint,
    string? GrpcSolidityEndpoint = null);

public static class TronNetwork
{
    public static readonly TronNetworkConfig Mainnet = new(
        "https://api.trongrid.io",
        "grpc.trongrid.io:50051",
        "grpc.trongrid.io:50061");

    public static readonly TronNetworkConfig Nile = new(
        "https://nile.trongrid.io",
        "grpc.nile.trongrid.io:50051",
        "grpc.nile.trongrid.io:50061");

    public static readonly TronNetworkConfig Shasta = new(
        "https://api.shasta.trongrid.io",
        "grpc.shasta.trongrid.io:50051",
        "grpc.shasta.trongrid.io:50061");
}
```

- [ ] **Step 2: Implement ITronProvider**

Interface with all methods per spec: GetAccountAsync, GetNowBlockAsync, GetBlockByNumAsync, CreateTransactionAsync, BroadcastTransactionAsync, GetTransactionByIdAsync, GetTransactionInfoByIdAsync, TriggerSmartContractAsync, TriggerConstantContractAsync, GetAccountResourceAsync, EstimateEnergyAsync.

- [ ] **Step 3: Verify build**
- [ ] **Step 4: Commit** "feat(tron): add TronNetwork config and ITronProvider interface"

---

### Task 3: TronHttpProvider

**Files:**
- Create: `src/ChainKit.Tron/Providers/TronHttpProvider.cs`
- Create: `tests/ChainKit.Tron.Tests/Providers/TronHttpProviderTests.cs`

- [ ] **Step 1: Write tests using mocked HttpClient**

Test that the provider correctly calls the right TronGrid endpoints and parses JSON responses. Use a custom HttpMessageHandler to mock HTTP responses.

Test cases:
- GetAccountAsync calls `/wallet/getaccount` with correct JSON body
- BroadcastTransactionAsync calls `/wallet/broadcasttransaction`
- GetTransactionByIdAsync calls `/wallet/gettransactionbyid`
- GetTransactionInfoByIdAsync calls `/walletsolidity/gettransactioninfobyid` (Solidity endpoint)

- [ ] **Step 2: Implement TronHttpProvider**

Uses `HttpClient` to call TronGrid HTTP API. Endpoints:
- Full Node: `{baseUrl}/wallet/{method}`
- Solidity: `{baseUrl}/walletsolidity/{method}`

Constructor: `TronHttpProvider(string baseUrl, string? apiKey = null)` and `TronHttpProvider(TronNetworkConfig network, string? apiKey = null)`.

If apiKey is provided, add header `TRON-PRO-API-KEY: {apiKey}`.

- [ ] **Step 3: Run tests, verify pass**
- [ ] **Step 4: Commit** "feat(tron): add TronHttpProvider for TronGrid HTTP API"

---

### Task 4: TronGrpcProvider

**Files:**
- Create: `src/ChainKit.Tron/Providers/TronGrpcProvider.cs`
- Create: `tests/ChainKit.Tron.Tests/Providers/TronGrpcProviderTests.cs`

- [ ] **Step 1: Write basic structure tests**

gRPC is harder to mock. Test constructor accepts endpoints correctly.

- [ ] **Step 2: Implement TronGrpcProvider**

Uses `Grpc.Net.Client.GrpcChannel` to connect to Full Node and optionally Solidity Node. The Tron gRPC API uses the protobuf service definitions. Since we trimmed the proto files, we may need to add gRPC service definitions or call via raw gRPC.

Constructor: `TronGrpcProvider(string fullNodeEndpoint, string? solidityEndpoint = null)` and `TronGrpcProvider(TronNetworkConfig network)`.

NOTE: For proto service definitions, we need to add `service Wallet { ... }` to our proto files or use manual gRPC calls. If proto service definitions are missing, implement using `Grpc.Core.CallInvoker` with manual method descriptors.

- [ ] **Step 3: Run tests, verify pass**
- [ ] **Step 4: Commit** "feat(tron): add TronGrpcProvider for Full Node gRPC"

---

### Task 5: TronClient — High-Level Facade

**Files:**
- Create: `src/ChainKit.Tron/TronClient.cs`
- Create: `tests/ChainKit.Tron.Tests/TronClientTests.cs`

- [ ] **Step 1: Write tests with mocked ITronProvider**

Add NSubstitute package to test project. Mock ITronProvider to test TronClient logic:
- TransferTrxAsync: verifies it calls GetNowBlockAsync → CreateTransfer → Sign → BroadcastAsync
- GetBalanceAsync: verifies it calls GetAccountAsync + TriggerConstantContractAsync for each TRC20
- GetTransactionDetailAsync: verifies it calls GetTransactionByIdAsync + GetTransactionInfoByIdAsync and merges results

- [ ] **Step 2: Implement TronClient**

All high-level methods per spec. Each method:
1. Catches any provider exception → returns TronResult.Fail
2. Integrates multiple provider calls
3. Converts Sun to TRX for high-level API
4. Returns TronResult<T>

- [ ] **Step 3: Run ALL tests**
- [ ] **Step 4: Commit** "feat(tron): add TronClient high-level facade"

---

### Task 6: Trc20Contract

**Files:**
- Create: `src/ChainKit.Tron/Contracts/Trc20Contract.cs`
- Create: `tests/ChainKit.Tron.Tests/Contracts/Trc20ContractTests.cs`

- [ ] **Step 1: Write tests with mocked ITronProvider**

Test read-only methods (NameAsync, SymbolAsync, BalanceOfAsync) and write methods (TransferAsync, MintAsync, BurnAsync) using mocked provider.

- [ ] **Step 2: Implement Trc20Contract**

Uses ITronProvider + AbiEncoder + TransactionBuilder internally. Read-only methods use TriggerConstantContractAsync. Write methods use TriggerSmartContractAsync → Sign → Broadcast.

Constructor takes ITronProvider, contractAddress, and TronAccount (for signing write operations).

- [ ] **Step 3: Run ALL tests**
- [ ] **Step 4: Commit** "feat(tron): add Trc20Contract for TRC20 token interaction"

---

### Task 7: Contract Deployment + Trc20Template

**Files:**
- Create: `src/ChainKit.Tron/Contracts/Trc20Template.cs`
- Modify: `src/ChainKit.Tron/TronClient.cs` (add DeployContractAsync, DeployTrc20TokenAsync)
- Create: `tests/ChainKit.Tron.Tests/Contracts/Trc20TemplateTests.cs`

- [ ] **Step 1: Create Trc20Template with standard bytecode**

The template contains pre-compiled Solidity bytecode for a standard TRC20 token with optional Mintable/Burnable features. For now, use a placeholder bytecode constant — the actual Solidity compilation will be done separately.

- [ ] **Step 2: Add deploy methods to TronClient**
- [ ] **Step 3: Write and run tests**
- [ ] **Step 4: Commit** "feat(tron): add TRC20 deployment template and deploy methods"

---

## Plan 2 Complete

After all 7 tasks:
- **Models**: All DTOs, TronResult, enums
- **Providers**: ITronProvider, TronHttpProvider, TronGrpcProvider, TronNetwork
- **TronClient**: High-level facade with all operations
- **Contracts**: Trc20Contract, Trc20Template, deployment

**Next:** Plan 3 (Watching — ZMQ + Polling)
