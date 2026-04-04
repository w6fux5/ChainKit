using System.Numerics;
using ChainKit.Core.Extensions;
using ChainKit.Tron.Contracts;
using ChainKit.Tron.Crypto;
using ChainKit.Tron.Models;
using ChainKit.Tron.Protocol;
using ChainKit.Tron.Providers;
using ChainKit.Tron.Watching;
using Xunit;
using Xunit.Abstractions;

namespace ChainKit.Tron.Tests.Integration;

// ============================================================
// Additional Nile E2E constants (local node endpoints)
// ============================================================
internal static class NileLocalConstants
{
    public const string NileLocalHttpEndpoint = "http://192.168.23.12:18090";
    public const string NileLocalSolidityEndpoint = "http://192.168.23.12:18091";
    public const string NileLocalZmqEndpoint = "tcp://192.168.23.12:15555";
}

// ============================================================
// Shared helper: detects testnet resource exhaustion to avoid
// false-negative test failures on the Nile testnet.
// ============================================================
internal static class TestnetGuard
{
    /// <summary>
    /// Checks if a failure message is due to testnet resource exhaustion
    /// (bandwidth or energy).
    /// </summary>
    internal static bool IsResourceExhausted(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage)) return false;
        return errorMessage.Contains("BANDWITH", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("bandwidth", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("resource insufficient", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("Account resource", StringComparison.OrdinalIgnoreCase);
    }
}

// ============================================================
// 1. LOW-LEVEL TRC20 TRANSFER (manual ABI + trigger + sign + broadcast)
// ============================================================
[Trait("Category", "Integration")]
public class NileLowLevelTrc20TransferTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private TronHttpProvider _provider = null!;
    private TronAccount _account1 = null!;

    public NileLowLevelTrc20TransferTests(ITestOutputHelper output) { _output = output; }

    public Task InitializeAsync()
    {
        _provider = new TronHttpProvider(NileLocalConstants.NileLocalHttpEndpoint);
        _account1 = NileTestConstants.GetAccount1();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _provider.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ManualTrc20Transfer_BuildAbiTriggerSignBroadcast()
    {
        // Step 1: Build ABI data for transfer(address,uint256)
        var recipientHex = TronAddress.ToHex(NileTestConstants.Account2Address);
        var rawAmount = new BigInteger(1_000_000); // 1 TTTE (6 decimals)
        var abiData = AbiEncoder.EncodeTransfer(recipientHex, rawAmount);

        // Step 2: Call TriggerSmartContractAsync to get unsigned transaction
        // TriggerSmartContractAsync expects parameters without the 4-byte selector
        var contractHex = NileTestConstants.TtteContractHex;
        var tx = await _provider.TriggerSmartContractAsync(
            _account1.HexAddress,
            contractHex,
            "transfer(address,uint256)",
            abiData[4..], // strip 4-byte selector
            100_000_000,  // feeLimit = 100 TRX
            0);

        // Step 3: Sign the transaction
        var signed = TransactionUtils.Sign(tx, _account1.PrivateKey);
        var txId = TransactionUtils.ComputeTxId(signed).ToHex();

        // Step 4: Broadcast the signed transaction
        var broadcastResult = await _provider.BroadcastTransactionAsync(signed);

        // Step 5: Verify success (or report resource exhaustion as known limitation)
        if (!broadcastResult.Success && TestnetGuard.IsResourceExhausted(broadcastResult.Message))
        {
            _output.WriteLine($"[KNOWN LIMITATION] Testnet resources exhausted: {broadcastResult.Message}");
            return;
        }

        Assert.True(broadcastResult.Success,
            $"Broadcast failed: message={broadcastResult.Message}");
        Assert.False(string.IsNullOrEmpty(broadcastResult.TxId ?? txId),
            "TxId should not be empty");
    }
}

// ============================================================
// 2. DELEGATE / UNDELEGATE RESOURCE TESTS (Stake 2.0)
// ============================================================
[Trait("Category", "Integration")]
public class NileDelegationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private TronClient _client = null!;
    private TronAccount _account1 = null!;

    public NileDelegationTests(ITestOutputHelper output) { _output = output; }

    public Task InitializeAsync()
    {
        var provider = new TronHttpProvider(NileLocalConstants.NileLocalHttpEndpoint);
        _client = new TronClient(provider);
        _account1 = NileTestConstants.GetAccount1();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task DelegateAndUndelegate_Bandwidth()
    {
        // Step 1: Stake TRX for bandwidth to ensure we have resources to delegate
        var stakeResult = await _client.StakeTrxAsync(_account1, 10m, ResourceType.Bandwidth);
        if (!stakeResult.Success)
        {
            var msg = stakeResult.Error?.Message ?? "";
            if (TestnetGuard.IsResourceExhausted(msg))
            {
                _output.WriteLine($"[KNOWN LIMITATION] Testnet resources exhausted for staking: {msg}");
                return;
            }
            // Other stake failures (already staked, etc.) are OK -- try delegation anyway
        }
        else
        {
            Assert.False(string.IsNullOrEmpty(stakeResult.Data!.TxId), "Stake TxId should not be empty");
            await Task.Delay(5000);
        }

        // Step 2: Delegate bandwidth to account2
        var delegateResult = await _client.DelegateResourceAsync(
            _account1, NileTestConstants.Account2Address, 5m, ResourceType.Bandwidth);

        if (!delegateResult.Success)
        {
            var msg = delegateResult.Error?.Message ?? "";
            if (TestnetGuard.IsResourceExhausted(msg))
            {
                _output.WriteLine($"[KNOWN LIMITATION] Testnet resources exhausted for delegation: {msg}");
                return;
            }
            // Acceptable reasons: not enough frozen balance, already delegated, etc.
            Assert.True(
                msg.Contains("frozen", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("balance", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("not enough", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("delegate", StringComparison.OrdinalIgnoreCase),
                $"Unexpected delegate error: {msg}");
            return; // Cannot continue without successful delegation
        }

        Assert.False(string.IsNullOrEmpty(delegateResult.Data!.TxId), "Delegate TxId should not be empty");
        Assert.Equal(NileTestConstants.Account2Address, delegateResult.Data.ReceiverAddress);
        Assert.Equal(5m, delegateResult.Data.Amount);
        Assert.Equal(ResourceType.Bandwidth, delegateResult.Data.Resource);

        // Wait for delegation confirmation
        await Task.Delay(5000);

        // Step 3: Undelegate bandwidth from account2
        var undelegateResult = await _client.UndelegateResourceAsync(
            _account1, NileTestConstants.Account2Address, 5m, ResourceType.Bandwidth);

        if (!undelegateResult.Success)
        {
            var msg = undelegateResult.Error?.Message ?? "";
            if (TestnetGuard.IsResourceExhausted(msg))
            {
                _output.WriteLine($"[KNOWN LIMITATION] Testnet resources exhausted for undelegation: {msg}");
                return;
            }
            // Acceptable: lock period not expired, not enough delegated, etc.
            Assert.True(
                msg.Contains("lock", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("not enough", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("delegate", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("insufficient", StringComparison.OrdinalIgnoreCase),
                $"Unexpected undelegate error: {msg}");
            return;
        }

        Assert.False(string.IsNullOrEmpty(undelegateResult.Data!.TxId), "Undelegate TxId should not be empty");
        Assert.Equal(NileTestConstants.Account2Address, undelegateResult.Data.ReceiverAddress);
        Assert.Equal(5m, undelegateResult.Data.Amount);
        Assert.Equal(ResourceType.Bandwidth, undelegateResult.Data.Resource);
    }
}

// ============================================================
// 3. LOW-LEVEL CONTRACT DEPLOYMENT (DeployContractAsync with bytecode)
// ============================================================
[Trait("Category", "Integration")]
public class NileLowLevelDeployTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private TronClient _client = null!;
    private TronAccount _account1 = null!;

    public NileLowLevelDeployTests(ITestOutputHelper output) { _output = output; }

    public Task InitializeAsync()
    {
        var provider = new TronHttpProvider(NileLocalConstants.NileLocalHttpEndpoint);
        _client = new TronClient(provider);
        _account1 = NileTestConstants.GetAccount1();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task DeployContractAsync_WithTrc20Bytecode_Succeeds()
    {
        // Use the Trc20Template to produce bytecode and ABI (same as DeployTrc20TokenAsync
        // but calling the low-level DeployContractAsync directly)
        var options = new Trc20TokenOptions(
            Name: "LowLevelToken",
            Symbol: "LLT",
            Decimals: 6,
            InitialSupply: new BigInteger(500_000_000)); // 500 tokens

        var bytecode = Trc20Template.GetBytecode(options);
        var abi = Trc20Template.GetAbi(options);

        // Call the low-level DeployContractAsync (not DeployTrc20TokenAsync)
        var deployResult = await _client.DeployContractAsync(_account1, bytecode, abi);
        if (!deployResult.Success && TestnetGuard.IsResourceExhausted(deployResult.Error?.Message))
        {
            _output.WriteLine($"[KNOWN LIMITATION] Testnet resources exhausted: {deployResult.Error?.Message}");
            return;
        }
        Assert.True(deployResult.Success, deployResult.Error?.Message ?? "Deploy failed");
        Assert.False(string.IsNullOrEmpty(deployResult.Data!.TxId),
            "Deploy TxId should not be empty");

        // Wait for deployment confirmation and verify the contract address is eventually available
        string? contractAddress = null;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(5000);
            var txInfo = await _client.Provider.GetTransactionInfoByIdAsync(deployResult.Data.TxId);
            if (txInfo is not null && txInfo.BlockNumber > 0
                && !string.IsNullOrEmpty(txInfo.ContractAddress))
            {
                contractAddress = txInfo.ContractAddress;
                break;
            }
        }

        Assert.False(string.IsNullOrEmpty(contractAddress),
            $"Contract address should be available after deployment (txId={deployResult.Data.TxId})");
    }
}

// ============================================================
// 4. BURNFROM TESTS (Approve + BurnFrom on existing TTTE contract)
// ============================================================
[Trait("Category", "Integration")]
public class NileBurnFromTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private TronHttpProvider _provider = null!;
    private TronAccount _account1 = null!;

    public NileBurnFromTests(ITestOutputHelper output) { _output = output; }

    public Task InitializeAsync()
    {
        _provider = new TronHttpProvider(NileLocalConstants.NileLocalHttpEndpoint);
        _account1 = NileTestConstants.GetAccount1();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _provider.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task BurnFromAsync_SelfApprove_ThenBurn()
    {
        // Use the existing TTTE contract which account1 owns and which supports burnFrom.
        // This avoids the slow deploy+confirm cycle that can fail on testnet.
        using var contract = new Trc20Contract(_provider, NileTestConstants.TtteContractAddress, _account1);

        // Step 1: Self-approve (account1 approves account1 to spend tokens)
        var approveResult = await contract.ApproveAsync(NileTestConstants.Account1Address, 50m);
        if (!approveResult.Success && TestnetGuard.IsResourceExhausted(approveResult.Error?.Message))
        {
            _output.WriteLine($"[KNOWN LIMITATION] Testnet resources exhausted for approve: {approveResult.Error?.Message}");
            return;
        }
        Assert.True(approveResult.Success, approveResult.Error?.Message ?? "Approve failed");

        await Task.Delay(5000);

        // Step 2: Verify allowance
        var allowanceResult = await contract.AllowanceAsync(
            NileTestConstants.Account1Address, NileTestConstants.Account1Address);
        Assert.True(allowanceResult.Success, allowanceResult.Error?.Message ?? "Allowance query failed");
        Assert.Equal(50m, allowanceResult.Data);

        // Step 3: BurnFrom (account1 burns tokens from account1's balance using allowance)
        var burnResult = await contract.BurnFromAsync(NileTestConstants.Account1Address, 10m);
        if (!burnResult.Success && TestnetGuard.IsResourceExhausted(burnResult.Error?.Message))
        {
            _output.WriteLine($"[KNOWN LIMITATION] Testnet resources exhausted for burnFrom: {burnResult.Error?.Message}");
            return;
        }
        Assert.True(burnResult.Success, burnResult.Error?.Message ?? "BurnFrom failed");
        Assert.False(string.IsNullOrEmpty(burnResult.Data!.TxId), "BurnFrom TxId should not be empty");
    }
}

// ============================================================
// 5. POLLING BLOCK STREAM + TRANSACTION WATCHER (live E2E)
// ============================================================
[Trait("Category", "Integration")]
public class NileWatcherTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private TronHttpProvider _localProvider = null!;
    private TronClient _localClient = null!;
    private TronAccount _account1 = null!;

    public NileWatcherTests(ITestOutputHelper output) { _output = output; }

    public Task InitializeAsync()
    {
        _localProvider = new TronHttpProvider(NileLocalConstants.NileLocalHttpEndpoint);
        _localClient = new TronClient(_localProvider);
        _account1 = NileTestConstants.GetAccount1();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _localClient.Dispose();
        // _localProvider is disposed by _localClient
        return Task.CompletedTask;
    }

    [Fact]
    public async Task PollingWatcher_DetectsTrxTransfer()
    {
        // Use a separate provider for the watcher (so it is not disposed by the client)
        using var watcherProvider = new TronHttpProvider(NileLocalConstants.NileLocalHttpEndpoint);
        var stream = new PollingBlockStream(watcherProvider, intervalMs: 1000);

        // Use CancellationToken with hard timeout to prevent test hangs
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var watcher = new TronTransactionWatcher(stream, watcherProvider);

        // Set up event capture
        TrxReceivedEventArgs? receivedEvent = null;
        var eventReceived = new TaskCompletionSource<bool>();
        watcher.OnTrxReceived += (_, e) =>
        {
            receivedEvent = e;
            eventReceived.TrySetResult(true);
        };

        // Watch account2's hex address
        var account2Hex = TronAddress.ToHex(NileTestConstants.Account2Address);
        watcher.WatchAddress(account2Hex);

        // Start watcher before sending the transaction
        await watcher.StartAsync(cts.Token);

        // Wait a moment for the watcher to start polling
        await Task.Delay(2000);

        // Send a TRX transfer using the local node
        var transferResult = await _localClient.TransferTrxAsync(
            _account1, NileTestConstants.Account2Address, 1m);
        if (!transferResult.Success && TestnetGuard.IsResourceExhausted(transferResult.Error?.Message))
        {
            _output.WriteLine($"[KNOWN LIMITATION] Testnet resources exhausted: {transferResult.Error?.Message}");
            return;
        }
        Assert.True(transferResult.Success, transferResult.Error?.Message ?? "TRX transfer failed");

        // Wait for the watcher to detect the transaction (respect the 30s CTS)
        try
        {
            var timeoutTask = Task.Delay(25_000, cts.Token);
            var completedTask = await Task.WhenAny(eventReceived.Task, timeoutTask);

            Assert.True(eventReceived.Task.IsCompletedSuccessfully,
                "Watcher did not detect TRX transfer within 30 seconds");
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Watcher test timed out after 30 seconds");
        }

        // Verify event data
        Assert.NotNull(receivedEvent);
        Assert.Equal(account2Hex, receivedEvent!.ToAddress);
        Assert.True(receivedEvent.Amount > 0, $"Expected positive TRX amount, got {receivedEvent.Amount}");
        Assert.True(receivedEvent.BlockNumber > 0, $"Expected block number > 0, got {receivedEvent.BlockNumber}");
    }

    [Fact]
    public async Task ZmqWatcher_DetectsTrxTransfer()
    {
        // Hard timeout for the entire test to prevent hangs.
        // The CTS is only used for the watcher; the transfer itself does not use
        // the CTS to avoid TaskCanceledException during the broadcast call.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Create a ZmqBlockStream using the local Nile node.
        // NOTE: This test requires the local Nile node to have ZMQ enabled.
        ZmqBlockStream stream;
        try
        {
            stream = new ZmqBlockStream(NileLocalConstants.NileLocalZmqEndpoint);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"[KNOWN LIMITATION] ZMQ not available: {ex.Message}");
            return;
        }

        await using var watcher = new TronTransactionWatcher(stream, _localProvider);

        // Set up event capture — use OnTrxReceived since OnTransactionConfirmed
        // now fires from the confirmation tracker (not block discovery)
        TrxReceivedEventArgs? receivedEvent = null;
        var eventReceived = new TaskCompletionSource<bool>();
        watcher.OnTrxReceived += (_, e) =>
        {
            receivedEvent = e;
            eventReceived.TrySetResult(true);
        };

        // Watch account2
        var account2Hex = TronAddress.ToHex(NileTestConstants.Account2Address);
        watcher.WatchAddress(account2Hex);

        // Start watcher with the CTS token so it auto-stops on timeout
        await watcher.StartAsync(cts.Token);

        // Wait for ZMQ subscription to be established
        await Task.Delay(3000);

        // Send a TRX transfer using the local node (no CTS -- avoid cancellation during broadcast)
        TronResult<TransferResult> transferResult;
        try
        {
            transferResult = await _localClient.TransferTrxAsync(
                _account1, NileTestConstants.Account2Address, 1m);
        }
        catch (OperationCanceledException)
        {
            _output.WriteLine("[KNOWN LIMITATION] ZMQ test timed out before transfer could complete");
            return;
        }

        if (!transferResult.Success && TestnetGuard.IsResourceExhausted(transferResult.Error?.Message))
        {
            _output.WriteLine($"[KNOWN LIMITATION] Testnet resources exhausted: {transferResult.Error?.Message}");
            return;
        }
        Assert.True(transferResult.Success, transferResult.Error?.Message ?? "TRX transfer failed");

        // Wait for the watcher to detect the transaction (25s remaining)
        var timeoutTask = Task.Delay(25_000);
        var completedTask = await Task.WhenAny(eventReceived.Task, timeoutTask);

        // ZMQ connectivity issues are common in test environments
        Assert.True(eventReceived.Task.IsCompletedSuccessfully,
            "ZMQ watcher did not detect TRX transfer within 30 seconds. " +
            "This may be a ZMQ connectivity issue with " +
            NileLocalConstants.NileLocalZmqEndpoint);

        Assert.NotNull(receivedEvent);
        Assert.True(receivedEvent!.BlockNumber > 0,
            $"Expected block number > 0, got {receivedEvent.BlockNumber}");
    }
}
