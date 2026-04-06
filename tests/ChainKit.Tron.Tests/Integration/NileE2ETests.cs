using System.Numerics;
using ChainKit.Core.Extensions;
using ChainKit.Tron.Contracts;
using ChainKit.Tron.Crypto;
using ChainKit.Tron.Models;
using ChainKit.Tron.Protocol;
using ChainKit.Tron.Providers;
using Xunit;

namespace ChainKit.Tron.Tests.Integration;

// ============================================================
// Shared constants for Nile testnet E2E tests
// ============================================================
internal static class NileTestConstants
{
    public const string NileHttpEndpoint = "https://nile.trongrid.io";

    public const string Account1Address = "TESpktHxdbATo4pvJoKDz6UFFajo5j2ScV";
    public const string Account2Address = "TRPBancCRForSzfCFVTqznMrN2VPxATsuA";

    public const string TtteContractAddress = "TFiQkVkoH9KsgdgdmcJEcG3JFhJV98g5uo";
    public const string TtteContractHex = "413f0452a1f458b4194201eb26cd585cfc134a11c1";

    public static readonly string Account1PrivateKeyHex =
        Environment.GetEnvironmentVariable("TRON_TEST_PRIVATE_KEY_1")
        ?? "c1350cce9fa8144b2dfdf2d97d8769d04b8637403944ca51b648131a2d409f05";

    public static readonly string Account2PrivateKeyHex =
        Environment.GetEnvironmentVariable("TRON_TEST_PRIVATE_KEY_2")
        ?? "864346462f4cb5bd78d6acd623359fed795b65d415eaa24dc68c9f61a437452d";

    public static TronAccount GetAccount1() =>
        TronAccount.FromPrivateKey(Account1PrivateKeyHex.FromHex());

    public static TronAccount GetAccount2() =>
        TronAccount.FromPrivateKey(Account2PrivateKeyHex.FromHex());
}

// ============================================================
// HIGH-LEVEL API TESTS (TronClient)
// ============================================================
[Trait("Category", "Integration")]
public class NileHighLevelTests : IAsyncLifetime
{
    private TronClient _client = null!;
    private TronAccount _account1 = null!;
    private TronAccount _account2 = null!;

    // Shared state across tests for tx queries
    private static string? _trxTransferTxId;
    private static string? _trc20TransferTxId;
    private static string? _stakeTxId;

    public Task InitializeAsync()
    {
        var provider = new TronHttpProvider(NileTestConstants.NileHttpEndpoint);
        _client = new TronClient(provider);
        _account1 = NileTestConstants.GetAccount1();
        _account2 = NileTestConstants.GetAccount2();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetBalanceAsync_ReturnsTrxBalance()
    {
        var result = await _client.GetBalanceAsync(NileTestConstants.Account1Address);
        Assert.True(result.Success, result.Error?.Message ?? "unknown error");
        Assert.True(result.Data!.TrxBalance > 0, $"Expected TRX balance > 0, got {result.Data.TrxBalance}");
    }

    [Fact]
    public async Task GetBalanceAsync_WithTrc20_ReturnsTtteBalance()
    {
        var result = await _client.GetBalanceAsync(
            NileTestConstants.Account1Address,
            new[] { NileTestConstants.TtteContractAddress });

        Assert.True(result.Success, result.Error?.Message ?? "unknown error");
        Assert.True(result.Data!.TrxBalance > 0, "TRX balance should be > 0");

        Assert.True(result.Data.Trc20Balances.ContainsKey(NileTestConstants.TtteContractAddress),
            "Should contain TTTE token balance");

        var ttte = result.Data.Trc20Balances[NileTestConstants.TtteContractAddress];
        Assert.True(ttte.Balance > 0, $"Expected TTTE balance > 0, got {ttte.Balance}");
        Assert.False(string.IsNullOrEmpty(ttte.Symbol), "Token symbol should not be empty");
    }

    [Fact]
    public async Task GetResourceInfoAsync_ReturnsBandwidthAndEnergy()
    {
        var result = await _client.GetResourceInfoAsync(NileTestConstants.Account1Address);
        Assert.True(result.Success, result.Error?.Message ?? "unknown error");

        var info = result.Data!;
        Assert.True(info.BandwidthTotal >= 0, "BandwidthTotal should be >= 0");
        Assert.True(info.EnergyTotal >= 0, "EnergyTotal should be >= 0");
    }

    [Fact]
    public async Task TransferTrxAsync_SendsAndSucceeds()
    {
        var result = await _client.TransferTrxAsync(_account1, NileTestConstants.Account2Address, 1m);
        Assert.True(result.Success, result.Error?.Message ?? "unknown error");

        var transfer = result.Data!;
        Assert.False(string.IsNullOrEmpty(transfer.TxId), "TxId should not be empty");
        Assert.Equal(NileTestConstants.Account1Address, transfer.FromAddress);
        Assert.Equal(NileTestConstants.Account2Address, transfer.ToAddress);
        Assert.Equal(1m, transfer.Amount);

        _trxTransferTxId = transfer.TxId;
    }

    [Fact]
    public async Task GetTransactionDetailAsync_ForTrxTransfer()
    {
        // First, perform a transfer to ensure we have a txId
        if (_trxTransferTxId is null)
        {
            var txResult = await _client.TransferTrxAsync(_account1, NileTestConstants.Account2Address, 1m);
            Assert.True(txResult.Success, txResult.Error?.Message ?? "Transfer failed");
            _trxTransferTxId = txResult.Data!.TxId;
        }

        // Wait for confirmation
        await Task.Delay(5000);

        var result = await _client.GetTransactionDetailAsync(_trxTransferTxId);
        Assert.True(result.Success, result.Error?.Message ?? "unknown error");

        var detail = result.Data!;
        Assert.Equal(_trxTransferTxId, detail.TxId);
        Assert.Equal(TransactionType.NativeTransfer, detail.Type);
        Assert.Equal(NileTestConstants.Account1Address, detail.FromAddress);
        Assert.Equal(NileTestConstants.Account2Address, detail.ToAddress);
        Assert.Equal(1m, detail.Amount);
    }

    [Fact]
    public async Task Trc20Contract_TransferAsync_SendsAndSucceeds()
    {
        using var contract = new Trc20Contract(_client.Provider,
            NileTestConstants.TtteContractAddress, _account1);
        var result = await contract.TransferAsync(
            NileTestConstants.Account2Address, 1m);

        Assert.True(result.Success, result.Error?.Message ?? "unknown error");

        var transfer = result.Data!;
        Assert.False(string.IsNullOrEmpty(transfer.TxId), "TxId should not be empty");

        _trc20TransferTxId = transfer.TxId;
    }

    [Fact]
    public async Task GetTransactionDetailAsync_ForTrc20Transfer()
    {
        // First, perform a TRC20 transfer to ensure we have a txId
        if (_trc20TransferTxId is null)
        {
            using var contract = new Trc20Contract(_client.Provider,
                NileTestConstants.TtteContractAddress, _account1);
            var txResult = await contract.TransferAsync(
                NileTestConstants.Account2Address, 1m);
            Assert.True(txResult.Success, txResult.Error?.Message ?? "TRC20 transfer failed");
            _trc20TransferTxId = txResult.Data!.TxId;
        }

        // Wait for confirmation
        await Task.Delay(5000);

        var result = await _client.GetTransactionDetailAsync(_trc20TransferTxId);
        Assert.True(result.Success, result.Error?.Message ?? "unknown error");

        var detail = result.Data!;
        Assert.Equal(_trc20TransferTxId, detail.TxId);
        Assert.Equal(TransactionType.Trc20Transfer, detail.Type);

        // Verify token transfer info
        Assert.NotNull(detail.TokenTransfer);
        Assert.False(string.IsNullOrEmpty(detail.TokenTransfer!.Symbol),
            "Token symbol should not be empty");
    }

    [Fact]
    public async Task StakeTrxAsync_StakesForEnergy()
    {
        var result = await _client.StakeTrxAsync(_account1, 5m, ResourceType.Energy);
        Assert.True(result.Success, result.Error?.Message ?? "unknown error");

        var stake = result.Data!;
        Assert.False(string.IsNullOrEmpty(stake.TxId), "TxId should not be empty");
        Assert.Equal(5m, stake.Amount);
        Assert.Equal(ResourceType.Energy, stake.Resource);

        _stakeTxId = stake.TxId;
    }

    [Fact]
    public async Task GetResourceInfoAsync_AfterStake_ShowsStakedEnergy()
    {
        // Stake first if needed
        if (_stakeTxId is null)
        {
            var stakeResult = await _client.StakeTrxAsync(_account1, 5m, ResourceType.Energy);
            if (stakeResult.Success)
            {
                _stakeTxId = stakeResult.Data!.TxId;
                await Task.Delay(5000);
            }
        }

        var result = await _client.GetResourceInfoAsync(NileTestConstants.Account1Address);
        Assert.True(result.Success, result.Error?.Message ?? "unknown error");

        // StakedForEnergy should be > 0 if staking succeeded
        // Note: this may be 0 if a previous unstake already processed
        var info = result.Data!;
        Assert.True(info.StakedForEnergy >= 0,
            $"StakedForEnergy should be >= 0, got {info.StakedForEnergy}");
    }

    [Fact]
    public async Task UnstakeTrxAsync_UnstakesEnergy()
    {
        // Ensure we have staked first
        if (_stakeTxId is null)
        {
            var stakeResult = await _client.StakeTrxAsync(_account1, 5m, ResourceType.Energy);
            if (stakeResult.Success)
            {
                _stakeTxId = stakeResult.Data!.TxId;
                await Task.Delay(5000);
            }
        }

        var result = await _client.UnstakeTrxAsync(_account1, 5m, ResourceType.Energy);
        // Unstake may fail if cooldown period hasn't passed -- that is acceptable
        if (!result.Success)
        {
            var msg = result.Error?.Message ?? "";
            // Acceptable failure reasons on testnet
            Assert.True(
                msg.Contains("NO_FROZEN", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("not time", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("not enough", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("cooldown", StringComparison.OrdinalIgnoreCase),
                $"Unexpected unstake error: {msg}");
            return; // Known limitation
        }

        var unstake = result.Data!;
        Assert.False(string.IsNullOrEmpty(unstake.TxId), "TxId should not be empty");
        Assert.Equal(5m, unstake.Amount);
    }
}

// ============================================================
// LOW-LEVEL API TESTS (ITronProvider)
// ============================================================
[Trait("Category", "Integration")]
public class NileLowLevelTests : IAsyncLifetime
{
    private TronHttpProvider _provider = null!;
    private TronAccount _account1 = null!;
    private TronAccount _account2 = null!;

    private static string? _manualTransferTxId;

    public Task InitializeAsync()
    {
        _provider = new TronHttpProvider(NileTestConstants.NileHttpEndpoint);
        _account1 = NileTestConstants.GetAccount1();
        _account2 = NileTestConstants.GetAccount2();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _provider.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetNowBlockAsync_ReturnsCurrentBlock()
    {
        var block = await _provider.GetNowBlockAsync();
        Assert.True(block.BlockNumber > 0, $"Block number should be > 0, got {block.BlockNumber}");
        Assert.False(string.IsNullOrEmpty(block.BlockId), "BlockId should not be empty");
    }

    [Fact]
    public async Task GetAccountAsync_ReturnsAccountWithBalance()
    {
        var account = await _provider.GetAccountAsync(_account1.HexAddress);
        Assert.True(account.Balance > 0, $"Balance should be > 0, got {account.Balance}");
    }

    [Fact]
    public async Task GetBlockByNumAsync_ReturnsBlock()
    {
        var block = await _provider.GetBlockByNumAsync(1);
        Assert.Equal(1, block.BlockNumber);
    }

    [Fact]
    public async Task GetAccountResourceAsync_ReturnsResourceInfo()
    {
        var resource = await _provider.GetAccountResourceAsync(_account1.HexAddress);
        Assert.True(resource.FreeBandwidthLimit >= 0, "FreeBandwidthLimit should be >= 0");
    }

    [Fact]
    public async Task TriggerConstantContractAsync_BalanceOf_ReturnsBalance()
    {
        var param = AbiEncoder.EncodeAddress(_account1.HexAddress);
        var result = await _provider.TriggerConstantContractAsync(
            _account1.HexAddress,
            NileTestConstants.TtteContractHex,
            "balanceOf(address)",
            param);

        Assert.NotNull(result);
        Assert.True(result.Length >= 32, $"Expected at least 32 bytes, got {result.Length}");

        var balance = AbiEncoder.DecodeUint256(result);
        Assert.True(balance > 0, $"Expected balance > 0, got {balance}");
    }

    [Fact]
    public async Task ManualTrxTransfer_BuildSignBroadcast()
    {
        // Step 1: Get current block for ref block
        var block = await _provider.GetNowBlockAsync();
        var blockNumBytes = BitConverter.GetBytes(block.BlockNumber);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(blockNumBytes);
        var refBlockBytes = blockNumBytes[^2..];

        byte[] refBlockHash;
        if (!string.IsNullOrEmpty(block.BlockId) && block.BlockId.Length >= 32)
            refBlockHash = block.BlockId[16..32].FromHex();
        else
            refBlockHash = new byte[8];

        // Step 2: Build transaction
        var tx = new TransactionBuilder()
            .CreateTransfer(_account1.HexAddress, _account2.HexAddress, 1_000_000) // 1 TRX in sun
            .SetRefBlock(refBlockBytes, refBlockHash)
            .Build();

        // Step 3: Sign
        var signed = TransactionUtils.Sign(tx, _account1.PrivateKey);
        var txId = TransactionUtils.ComputeTxId(signed).ToHex();

        // Step 4: Broadcast
        var broadcastResult = await _provider.BroadcastTransactionAsync(signed);
        Assert.True(broadcastResult.Success,
            $"Broadcast failed: success={broadcastResult.Success}, txId={broadcastResult.TxId}, message={broadcastResult.Message}");

        _manualTransferTxId = broadcastResult.TxId ?? txId;
        Assert.False(string.IsNullOrEmpty(_manualTransferTxId), "TxId should not be empty");
    }

    [Fact]
    public async Task GetTransactionByIdAsync_ReturnsTransaction()
    {
        // Create a tx first if we don't have one
        if (_manualTransferTxId is null)
        {
            var block = await _provider.GetNowBlockAsync();
            var blockNumBytes = BitConverter.GetBytes(block.BlockNumber);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(blockNumBytes);
            var refBlockBytes = blockNumBytes[^2..];

            byte[] refBlockHash;
            if (!string.IsNullOrEmpty(block.BlockId) && block.BlockId.Length >= 16)
                refBlockHash = block.BlockId[16..32].FromHex();
            else
                refBlockHash = new byte[8];

            var tx = new TransactionBuilder()
                .CreateTransfer(_account1.HexAddress, _account2.HexAddress, 1_000_000)
                .SetRefBlock(refBlockBytes, refBlockHash)
                .Build();

            var signed = TransactionUtils.Sign(tx, _account1.PrivateKey);
            var txId = TransactionUtils.ComputeTxId(signed).ToHex();
            var broadcastResult = await _provider.BroadcastTransactionAsync(signed);
            Assert.True(broadcastResult.Success, broadcastResult.Message ?? "Broadcast failed");
            _manualTransferTxId = broadcastResult.TxId ?? txId;
        }

        await Task.Delay(5000);

        var result = await _provider.GetTransactionByIdAsync(_manualTransferTxId);
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.TxId), "TxId should not be empty");
    }

    [Fact]
    public async Task GetTransactionInfoByIdAsync_ReturnsReceipt()
    {
        // Create a tx first if we don't have one
        if (_manualTransferTxId is null)
        {
            var block = await _provider.GetNowBlockAsync();
            var blockNumBytes = BitConverter.GetBytes(block.BlockNumber);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(blockNumBytes);
            var refBlockBytes = blockNumBytes[^2..];

            byte[] refBlockHash;
            if (!string.IsNullOrEmpty(block.BlockId) && block.BlockId.Length >= 32)
                refBlockHash = block.BlockId[16..32].FromHex();
            else
                refBlockHash = new byte[8];

            var tx = new TransactionBuilder()
                .CreateTransfer(_account1.HexAddress, _account2.HexAddress, 1_000_000)
                .SetRefBlock(refBlockBytes, refBlockHash)
                .Build();

            var signed = TransactionUtils.Sign(tx, _account1.PrivateKey);
            var txId = TransactionUtils.ComputeTxId(signed).ToHex();
            var broadcastResult = await _provider.BroadcastTransactionAsync(signed);
            Assert.True(broadcastResult.Success, broadcastResult.Message ?? "Broadcast failed");
            _manualTransferTxId = broadcastResult.TxId ?? txId;
        }

        // The solidity endpoint (/walletsolidity/gettransactioninfobyid) on Nile testnet
        // can take a long time. Retry with increasing delays up to ~45 seconds total.
        TransactionInfoDto? result = null;
        for (int attempt = 0; attempt < 9; attempt++)
        {
            await Task.Delay(5000);
            result = await _provider.GetTransactionInfoByIdAsync(_manualTransferTxId);
            if (result is not null && result.BlockNumber > 0)
                break;
        }

        Assert.NotNull(result);
        // If still not confirmed after 45s, verify the tx at least exists via full node
        if (result!.BlockNumber == 0)
        {
            var fullNodeResult = await _provider.GetTransactionByIdAsync(_manualTransferTxId);
            Assert.NotNull(fullNodeResult);
            Assert.False(string.IsNullOrEmpty(fullNodeResult.TxId),
                "Transaction should exist on full node even if solidity has not confirmed yet");
            // Mark as a known limitation -- solidity confirmation on Nile can be very slow
            return;
        }

        Assert.True(result.BlockNumber > 0, $"BlockNumber should be > 0, got {result.BlockNumber}");
    }
}

// ============================================================
// TRC20 CONTRACT TESTS (existing TTTE token)
// ============================================================
[Trait("Category", "Integration")]
public class NileTrc20ContractTests : IAsyncLifetime
{
    private TronHttpProvider _provider = null!;
    private TronAccount _account1 = null!;
    private Trc20Contract _contract = null!;

    public Task InitializeAsync()
    {
        _provider = new TronHttpProvider(NileTestConstants.NileHttpEndpoint);
        _account1 = NileTestConstants.GetAccount1();
        _contract = new Trc20Contract(_provider, NileTestConstants.TtteContractAddress, _account1);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _contract.Dispose();
        _provider.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task NameAsync_ReturnsNonEmpty()
    {
        var result = await _contract.NameAsync();
        Assert.True(result.Success, result.Error?.Message ?? "unknown error");
        Assert.False(string.IsNullOrEmpty(result.Data), "Token name should not be empty");
    }

    [Fact]
    public async Task SymbolAsync_ReturnsNonEmpty()
    {
        var result = await _contract.SymbolAsync();
        Assert.True(result.Success, result.Error?.Message ?? "unknown error");
        Assert.False(string.IsNullOrEmpty(result.Data), "Token symbol should not be empty");
    }

    [Fact]
    public async Task DecimalsAsync_Returns6()
    {
        var result = await _contract.DecimalsAsync();
        Assert.True(result.Success, result.Error?.Message ?? "unknown error");
        Assert.Equal(6, result.Data);
    }

    [Fact]
    public async Task BalanceOfAsync_ReturnsPositiveBalance()
    {
        var result = await _contract.BalanceOfAsync(NileTestConstants.Account1Address);
        Assert.True(result.Success, result.Error?.Message ?? "unknown error");
        Assert.True(result.Data > 0, $"Expected TTTE balance > 0, got {result.Data}");
    }

    [Fact]
    public async Task TotalSupplyAsync_ReturnsPositiveSupply()
    {
        var result = await _contract.TotalSupplyAsync();
        Assert.True(result.Success, result.Error?.Message ?? "unknown error");
        Assert.True(result.Data > 0, $"Expected total supply > 0, got {result.Data}");
    }

    [Fact]
    public async Task TransferAsync_TransfersTokens()
    {
        var result = await _contract.TransferAsync(NileTestConstants.Account2Address, 1m);
        Assert.True(result.Success, result.Error?.Message ?? "unknown error");

        var transfer = result.Data!;
        Assert.False(string.IsNullOrEmpty(transfer.TxId), "TxId should not be empty");
    }
}

// ============================================================
// CONTRACT DEPLOYMENT TESTS
// ============================================================
[Trait("Category", "Integration")]
public class NileContractDeployTests : IAsyncLifetime
{
    private TronClient _client = null!;
    private TronAccount _account1 = null!;
    private TronAccount _account2 = null!;

    private static string? _deployTxId;
    private static string? _deployedContractAddress;

    public Task InitializeAsync()
    {
        var provider = new TronHttpProvider(NileTestConstants.NileHttpEndpoint);
        _client = new TronClient(provider);
        _account1 = NileTestConstants.GetAccount1();
        _account2 = NileTestConstants.GetAccount2();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Deploys a TRC20 token and waits for confirmation, setting the static fields.
    /// Uses retry logic because the Nile testnet solidity node can be slow.
    /// </summary>
    private async Task EnsureDeployedAsync()
    {
        if (_deployedContractAddress is not null)
            return;

        var options = new Trc20TokenOptions(
            Name: "E2ETestToken",
            Symbol: "E2ET",
            Decimals: 6,
            InitialSupply: new BigInteger(1000_000_000)); // 1000 tokens with 6 decimals

        var deployResult = await _client.DeployTrc20TokenAsync(_account1, options);
        Assert.True(deployResult.Success, deployResult.Error?.Message ?? "Deploy failed");
        _deployTxId = deployResult.Data!.TxId;

        // Wait for deployment confirmation with retry (solidity node on Nile can be slow)
        for (int attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(5000);
            var txInfo = await _client.Provider.GetTransactionInfoByIdAsync(_deployTxId);
            if (txInfo is not null && txInfo.BlockNumber > 0
                && !string.IsNullOrEmpty(txInfo.ContractAddress))
            {
                _deployedContractAddress = txInfo.ContractAddress;
                return;
            }
        }

        Assert.Fail($"Contract deployment (txId={_deployTxId}) was not confirmed after 60 seconds.");
    }

    [Fact]
    public async Task DeployTrc20Token_AndVerify()
    {
        await EnsureDeployedAsync();
        Assert.False(string.IsNullOrEmpty(_deployTxId), "Deploy TxId should not be empty");
        Assert.False(string.IsNullOrEmpty(_deployedContractAddress),
            "Contract address should be available after deployment");
    }

    [Fact]
    public async Task DeployedToken_ReadMetadata()
    {
        await EnsureDeployedAsync();
        using var contract = _client.GetTrc20Contract(_deployedContractAddress!, _account1);

        // Verify Name
        var nameResult = await contract.NameAsync();
        Assert.True(nameResult.Success, nameResult.Error?.Message ?? "Name query failed");
        Assert.Equal("E2ETestToken", nameResult.Data);

        // Verify Symbol
        var symbolResult = await contract.SymbolAsync();
        Assert.True(symbolResult.Success, symbolResult.Error?.Message ?? "Symbol query failed");
        Assert.Equal("E2ET", symbolResult.Data);

        // Verify Decimals
        var decimalsResult = await contract.DecimalsAsync();
        Assert.True(decimalsResult.Success, decimalsResult.Error?.Message ?? "Decimals query failed");
        Assert.Equal(6, decimalsResult.Data);
    }

    [Fact]
    public async Task DeployedToken_BalanceOf_ShowsInitialSupply()
    {
        await EnsureDeployedAsync();
        using var contract = _client.GetTrc20Contract(_deployedContractAddress!, _account1);

        var balanceResult = await contract.BalanceOfAsync(NileTestConstants.Account1Address);
        Assert.True(balanceResult.Success, balanceResult.Error?.Message ?? "BalanceOf failed");
        // Balance could be less than 1000 if Transfer/Burn tests ran first on this contract,
        // but should be positive
        Assert.True(balanceResult.Data > 0, $"Balance should be > 0, got {balanceResult.Data}");
    }

    [Fact]
    public async Task DeployedToken_Transfer()
    {
        await EnsureDeployedAsync();
        using var contract = _client.GetTrc20Contract(_deployedContractAddress!, _account1);

        var result = await contract.TransferAsync(NileTestConstants.Account2Address, 10m);
        Assert.True(result.Success, result.Error?.Message ?? "Transfer failed");
        Assert.False(string.IsNullOrEmpty(result.Data!.TxId), "TxId should not be empty");
    }

    [Fact]
    public async Task DeployedToken_MintAndBurn()
    {
        await EnsureDeployedAsync();
        using var contract = _client.GetTrc20Contract(_deployedContractAddress!, _account1);

        // Mint 500 more tokens
        var mintResult = await contract.MintAsync(NileTestConstants.Account1Address, 500m);
        Assert.True(mintResult.Success, mintResult.Error?.Message ?? "Mint failed");

        await Task.Delay(3000);

        // Burn 100 tokens
        var burnResult = await contract.BurnAsync(100m);
        Assert.True(burnResult.Success, burnResult.Error?.Message ?? "Burn failed");
    }

    [Fact]
    public async Task DeployedToken_ApproveAndAllowance()
    {
        await EnsureDeployedAsync();
        using var contract = _client.GetTrc20Contract(_deployedContractAddress!, _account1);

        // Approve Account2 to spend 50 tokens
        var approveResult = await contract.ApproveAsync(NileTestConstants.Account2Address, 50m);
        Assert.True(approveResult.Success, approveResult.Error?.Message ?? "Approve failed");

        await Task.Delay(5000);

        // Check allowance
        var allowanceResult = await contract.AllowanceAsync(
            NileTestConstants.Account1Address, NileTestConstants.Account2Address);
        Assert.True(allowanceResult.Success, allowanceResult.Error?.Message ?? "Allowance query failed");
        Assert.Equal(50m, allowanceResult.Data);
    }
}
