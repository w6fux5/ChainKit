using System.Numerics;
using ChainKit.Core.Extensions;
using ChainKit.Tron.Contracts;
using ChainKit.Tron.Crypto;
using ChainKit.Tron.Models;
using ChainKit.Tron.Protocol;
using ChainKit.Tron.Protocol.Protobuf;
using ChainKit.Tron.Providers;

namespace ChainKit.Tron;

/// <summary>
/// High-level facade that integrates multiple low-level provider calls into single operations.
/// All public methods return <see cref="TronResult{T}"/>.
/// </summary>
public class TronClient
{
    private const long SunPerTrx = 1_000_000;
    private const long DefaultFeeLimit = 100_000_000; // 100 TRX

    public ITronProvider Provider { get; }

    public TronClient(ITronProvider provider)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    // === Transfer ===

    /// <summary>
    /// Transfers TRX from one account to another.
    /// Flow: getNowBlock -> build tx with ref block -> sign -> broadcast
    /// </summary>
    public async Task<TronResult<TransferResult>> TransferTrxAsync(
        TronAccount from, string toAddress, decimal trxAmount, CancellationToken ct = default)
    {
        try
        {
            var amountSun = (long)(trxAmount * SunPerTrx);
            var toHex = ResolveHexAddress(toAddress);

            var block = await Provider.GetNowBlockAsync(ct);
            var (refBlockBytes, refBlockHash) = ExtractRefBlock(block);

            var tx = new TransactionBuilder()
                .CreateTransfer(from.HexAddress, toHex, amountSun)
                .SetRefBlock(refBlockBytes, refBlockHash)
                .Build();

            var signed = TransactionUtils.Sign(tx, from.PrivateKey);
            var txId = TransactionUtils.ComputeTxId(signed).ToHex();

            var broadcastResult = await Provider.BroadcastTransactionAsync(signed, ct);

            if (!broadcastResult.Success)
            {
                var errorCode = MapBroadcastError(broadcastResult.Message);
                return TronResult<TransferResult>.Fail(errorCode,
                    broadcastResult.Message ?? "Broadcast failed", broadcastResult.Message);
            }

            return TronResult<TransferResult>.Ok(
                new TransferResult(broadcastResult.TxId ?? txId, from.Address, toAddress, trxAmount));
        }
        catch (Exception ex)
        {
            return TronResult<TransferResult>.Fail(
                TronErrorCode.ProviderConnectionFailed, ex.Message, ex.ToString());
        }
    }

    /// <summary>
    /// Transfers TRC20 tokens.
    /// Flow: encode ABI -> triggerSmartContract -> sign -> broadcast
    /// </summary>
    public async Task<TronResult<TransferResult>> TransferTrc20Async(
        TronAccount from, string contractAddress,
        string toAddress, decimal amount, int decimals, CancellationToken ct = default)
    {
        try
        {
            var toHex = ResolveHexAddress(toAddress);
            var contractHex = ResolveHexAddress(contractAddress);
            var rawAmount = new BigInteger(amount * (decimal)Math.Pow(10, decimals));
            var data = AbiEncoder.EncodeTransfer(toHex, rawAmount);

            var block = await Provider.GetNowBlockAsync(ct);
            var (refBlockBytes, refBlockHash) = ExtractRefBlock(block);

            // Use TriggerSmartContractAsync to get the transaction from the node
            var tx = await Provider.TriggerSmartContractAsync(
                from.HexAddress, contractHex, "transfer(address,uint256)",
                data[4..], // strip selector — provider adds it
                DefaultFeeLimit, 0, ct);

            // Set ref block on the returned transaction
            tx.RawData.RefBlockBytes = Google.Protobuf.ByteString.CopyFrom(refBlockBytes);
            tx.RawData.RefBlockHash = Google.Protobuf.ByteString.CopyFrom(refBlockHash);

            var signed = TransactionUtils.Sign(tx, from.PrivateKey);
            var txId = TransactionUtils.ComputeTxId(signed).ToHex();

            var broadcastResult = await Provider.BroadcastTransactionAsync(signed, ct);

            if (!broadcastResult.Success)
            {
                var errorCode = MapBroadcastError(broadcastResult.Message);
                return TronResult<TransferResult>.Fail(errorCode,
                    broadcastResult.Message ?? "Broadcast failed", broadcastResult.Message);
            }

            return TronResult<TransferResult>.Ok(
                new TransferResult(broadcastResult.TxId ?? txId, from.Address, toAddress, amount));
        }
        catch (Exception ex)
        {
            return TronResult<TransferResult>.Fail(
                TronErrorCode.ProviderConnectionFailed, ex.Message, ex.ToString());
        }
    }

    // === Query ===

    /// <summary>
    /// Gets detailed transaction information by merging Full Node and Solidity node results.
    /// </summary>
    public async Task<TronResult<TronTransactionDetail>> GetTransactionDetailAsync(
        string txId, CancellationToken ct = default)
    {
        try
        {
            // Step 1: Get transaction from Full Node
            var txInfo = await Provider.GetTransactionByIdAsync(txId, ct);
            if (txInfo is null || string.IsNullOrEmpty(txInfo.TxId))
            {
                return TronResult<TronTransactionDetail>.Fail(
                    TronErrorCode.Unknown, "Transaction not found");
            }

            // Step 2: Get transaction info from Solidity node
            TransactionInfoDto? solidityInfo = null;
            try
            {
                solidityInfo = await Provider.GetTransactionInfoByIdAsync(txId, ct);
            }
            catch
            {
                // Solidity info not available yet — treat as unconfirmed
            }

            var status = DetermineStatus(txInfo, solidityInfo);
            var type = DetermineTransactionType(txInfo);
            var blockNumber = solidityInfo?.BlockNumber ?? txInfo.BlockNumber;
            var timestamp = solidityInfo?.BlockTimestamp > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(solidityInfo.BlockTimestamp)
                : txInfo.BlockTimestamp > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(txInfo.BlockTimestamp)
                    : (DateTimeOffset?)null;

            FailureInfo? failure = null;
            if (status == TransactionStatus.Failed)
            {
                failure = new FailureInfo(
                    ParseFailureReason(txInfo.ContractResult),
                    txInfo.ContractResult ?? "Unknown failure",
                    null, txInfo.ContractResult);
            }

            var cost = solidityInfo is not null
                ? new ResourceCost(
                    (decimal)solidityInfo.Fee / SunPerTrx,
                    solidityInfo.NetUsage,
                    solidityInfo.EnergyUsage,
                    0m, 0m)
                : null;

            var detail = new TronTransactionDetail(
                TxId: txId,
                FromAddress: string.Empty,
                ToAddress: string.Empty,
                Status: status,
                Failure: failure,
                Type: type,
                Amount: 0m,
                TokenTransfer: null,
                BlockNumber: blockNumber > 0 ? blockNumber : null,
                Timestamp: timestamp,
                Cost: cost);

            return TronResult<TronTransactionDetail>.Ok(detail);
        }
        catch (Exception ex)
        {
            return TronResult<TronTransactionDetail>.Fail(
                TronErrorCode.ProviderConnectionFailed, ex.Message, ex.ToString());
        }
    }

    /// <summary>
    /// Gets the TRX balance and optionally TRC20 balances for an address.
    /// </summary>
    public async Task<TronResult<BalanceInfo>> GetBalanceAsync(
        string address, params string[] trc20Contracts)
    {
        return await GetBalanceAsync(address, trc20Contracts, default);
    }

    /// <summary>
    /// Gets the TRX balance and optionally TRC20 balances for an address.
    /// </summary>
    public async Task<TronResult<BalanceInfo>> GetBalanceAsync(
        string address, string[] trc20Contracts, CancellationToken ct)
    {
        try
        {
            var hexAddress = ResolveHexAddress(address);

            var accountInfo = await Provider.GetAccountAsync(hexAddress, ct);
            var trxBalance = (decimal)accountInfo.Balance / SunPerTrx;

            var trc20Balances = new Dictionary<string, decimal>();

            foreach (var contract in trc20Contracts)
            {
                try
                {
                    var contractHex = ResolveHexAddress(contract);
                    var data = AbiEncoder.EncodeBalanceOf(hexAddress);
                    var result = await Provider.TriggerConstantContractAsync(
                        hexAddress, contractHex, "balanceOf(address)", data[4..], ct);

                    var balance = result.Length >= 32
                        ? AbiEncoder.DecodeUint256(result)
                        : BigInteger.Zero;

                    // We don't know the decimals here, store raw value as decimal
                    trc20Balances[contract] = (decimal)balance;
                }
                catch
                {
                    // If a particular TRC20 query fails, report zero
                    trc20Balances[contract] = 0m;
                }
            }

            return TronResult<BalanceInfo>.Ok(
                new BalanceInfo(trxBalance, trc20Balances));
        }
        catch (Exception ex)
        {
            return TronResult<BalanceInfo>.Fail(
                TronErrorCode.ProviderConnectionFailed, ex.Message, ex.ToString());
        }
    }

    // === Resource Management (Staking 2.0) ===

    /// <summary>
    /// Stakes TRX for bandwidth or energy (Stake 2.0).
    /// </summary>
    public async Task<TronResult<StakeResult>> StakeTrxAsync(
        TronAccount account, decimal trxAmount, ResourceType resource, CancellationToken ct = default)
    {
        try
        {
            var amountSun = (long)(trxAmount * SunPerTrx);
            var resourceCode = MapResourceType(resource);

            var block = await Provider.GetNowBlockAsync(ct);
            var (refBlockBytes, refBlockHash) = ExtractRefBlock(block);

            var tx = new TransactionBuilder()
                .FreezeBalanceV2(account.HexAddress, amountSun, resourceCode)
                .SetRefBlock(refBlockBytes, refBlockHash)
                .Build();

            var signed = TransactionUtils.Sign(tx, account.PrivateKey);
            var txId = TransactionUtils.ComputeTxId(signed).ToHex();

            var broadcastResult = await Provider.BroadcastTransactionAsync(signed, ct);

            if (!broadcastResult.Success)
            {
                return TronResult<StakeResult>.Fail(
                    MapBroadcastError(broadcastResult.Message),
                    broadcastResult.Message ?? "Broadcast failed", broadcastResult.Message);
            }

            return TronResult<StakeResult>.Ok(
                new StakeResult(broadcastResult.TxId ?? txId, trxAmount, resource));
        }
        catch (Exception ex)
        {
            return TronResult<StakeResult>.Fail(
                TronErrorCode.ProviderConnectionFailed, ex.Message, ex.ToString());
        }
    }

    /// <summary>
    /// Unstakes TRX (Stake 2.0).
    /// </summary>
    public async Task<TronResult<UnstakeResult>> UnstakeTrxAsync(
        TronAccount account, decimal trxAmount, ResourceType resource, CancellationToken ct = default)
    {
        try
        {
            var amountSun = (long)(trxAmount * SunPerTrx);
            var resourceCode = MapResourceType(resource);

            var block = await Provider.GetNowBlockAsync(ct);
            var (refBlockBytes, refBlockHash) = ExtractRefBlock(block);

            var tx = new TransactionBuilder()
                .UnfreezeBalanceV2(account.HexAddress, amountSun, resourceCode)
                .SetRefBlock(refBlockBytes, refBlockHash)
                .Build();

            var signed = TransactionUtils.Sign(tx, account.PrivateKey);
            var txId = TransactionUtils.ComputeTxId(signed).ToHex();

            var broadcastResult = await Provider.BroadcastTransactionAsync(signed, ct);

            if (!broadcastResult.Success)
            {
                return TronResult<UnstakeResult>.Fail(
                    MapBroadcastError(broadcastResult.Message),
                    broadcastResult.Message ?? "Broadcast failed", broadcastResult.Message);
            }

            return TronResult<UnstakeResult>.Ok(
                new UnstakeResult(broadcastResult.TxId ?? txId, trxAmount, resource));
        }
        catch (Exception ex)
        {
            return TronResult<UnstakeResult>.Fail(
                TronErrorCode.ProviderConnectionFailed, ex.Message, ex.ToString());
        }
    }

    /// <summary>
    /// Delegates resources (bandwidth/energy) to another address (Stake 2.0).
    /// </summary>
    public async Task<TronResult<DelegateResult>> DelegateResourceAsync(
        TronAccount account, string receiverAddress,
        decimal trxAmount, ResourceType resource,
        bool lockPeriod = false, CancellationToken ct = default)
    {
        try
        {
            var amountSun = (long)(trxAmount * SunPerTrx);
            var receiverHex = ResolveHexAddress(receiverAddress);
            var resourceCode = MapResourceType(resource);

            var block = await Provider.GetNowBlockAsync(ct);
            var (refBlockBytes, refBlockHash) = ExtractRefBlock(block);

            var tx = new TransactionBuilder()
                .DelegateResource(account.HexAddress, receiverHex, amountSun, resourceCode, lockPeriod)
                .SetRefBlock(refBlockBytes, refBlockHash)
                .Build();

            var signed = TransactionUtils.Sign(tx, account.PrivateKey);
            var txId = TransactionUtils.ComputeTxId(signed).ToHex();

            var broadcastResult = await Provider.BroadcastTransactionAsync(signed, ct);

            if (!broadcastResult.Success)
            {
                return TronResult<DelegateResult>.Fail(
                    MapBroadcastError(broadcastResult.Message),
                    broadcastResult.Message ?? "Broadcast failed", broadcastResult.Message);
            }

            return TronResult<DelegateResult>.Ok(
                new DelegateResult(broadcastResult.TxId ?? txId, receiverAddress, trxAmount, resource));
        }
        catch (Exception ex)
        {
            return TronResult<DelegateResult>.Fail(
                TronErrorCode.ProviderConnectionFailed, ex.Message, ex.ToString());
        }
    }

    /// <summary>
    /// Undelegates resources from another address (Stake 2.0).
    /// </summary>
    public async Task<TronResult<UndelegateResult>> UndelegateResourceAsync(
        TronAccount account, string receiverAddress,
        decimal trxAmount, ResourceType resource, CancellationToken ct = default)
    {
        try
        {
            var amountSun = (long)(trxAmount * SunPerTrx);
            var receiverHex = ResolveHexAddress(receiverAddress);
            var resourceCode = MapResourceType(resource);

            var block = await Provider.GetNowBlockAsync(ct);
            var (refBlockBytes, refBlockHash) = ExtractRefBlock(block);

            var tx = new TransactionBuilder()
                .UndelegateResource(account.HexAddress, receiverHex, amountSun, resourceCode)
                .SetRefBlock(refBlockBytes, refBlockHash)
                .Build();

            var signed = TransactionUtils.Sign(tx, account.PrivateKey);
            var txId = TransactionUtils.ComputeTxId(signed).ToHex();

            var broadcastResult = await Provider.BroadcastTransactionAsync(signed, ct);

            if (!broadcastResult.Success)
            {
                return TronResult<UndelegateResult>.Fail(
                    MapBroadcastError(broadcastResult.Message),
                    broadcastResult.Message ?? "Broadcast failed", broadcastResult.Message);
            }

            return TronResult<UndelegateResult>.Ok(
                new UndelegateResult(broadcastResult.TxId ?? txId, receiverAddress, trxAmount, resource));
        }
        catch (Exception ex)
        {
            return TronResult<UndelegateResult>.Fail(
                TronErrorCode.ProviderConnectionFailed, ex.Message, ex.ToString());
        }
    }

    /// <summary>
    /// Gets resource information (bandwidth, energy, staking) for an address.
    /// </summary>
    public async Task<TronResult<ResourceInfo>> GetResourceInfoAsync(
        string address, CancellationToken ct = default)
    {
        try
        {
            var hexAddress = ResolveHexAddress(address);
            var resourceInfo = await Provider.GetAccountResourceAsync(hexAddress, ct);

            return TronResult<ResourceInfo>.Ok(new ResourceInfo(
                BandwidthTotal: resourceInfo.FreeBandwidthLimit + resourceInfo.TotalBandwidthLimit,
                BandwidthUsed: resourceInfo.FreeBandwidthUsed + resourceInfo.TotalBandwidthUsed,
                EnergyTotal: resourceInfo.EnergyLimit,
                EnergyUsed: resourceInfo.EnergyUsed,
                StakedForBandwidth: 0m, // Would require additional account query
                StakedForEnergy: 0m,
                DelegationsOut: Array.Empty<DelegationInfo>(),
                DelegationsIn: Array.Empty<DelegationInfo>()));
        }
        catch (Exception ex)
        {
            return TronResult<ResourceInfo>.Fail(
                TronErrorCode.ProviderConnectionFailed, ex.Message, ex.ToString());
        }
    }

    // === Contract Deployment ===

    /// <summary>
    /// Deploys a smart contract with the given bytecode and ABI.
    /// Flow: build deploy tx with ref block -> sign -> broadcast -> extract contract address.
    /// </summary>
    public async Task<TronResult<DeployResult>> DeployContractAsync(
        TronAccount account, byte[] bytecode, string abi,
        long feeLimit = DefaultFeeLimit, CancellationToken ct = default)
    {
        try
        {
            var block = await Provider.GetNowBlockAsync(ct);
            var (refBlockBytes, refBlockHash) = ExtractRefBlock(block);

            var tx = new TransactionBuilder()
                .CreateDeployContract(account.HexAddress, bytecode, abi)
                .SetFeeLimit(feeLimit)
                .SetRefBlock(refBlockBytes, refBlockHash)
                .Build();

            var signed = TransactionUtils.Sign(tx, account.PrivateKey);
            var txId = TransactionUtils.ComputeTxId(signed).ToHex();

            var broadcastResult = await Provider.BroadcastTransactionAsync(signed, ct);

            if (!broadcastResult.Success)
            {
                var errorCode = MapBroadcastError(broadcastResult.Message);
                return TronResult<DeployResult>.Fail(errorCode,
                    broadcastResult.Message ?? "Broadcast failed", broadcastResult.Message);
            }

            // Contract address is derived from the deployer address + txId.
            // On TRON the contract address is not directly returned by broadcast;
            // it can be fetched later via getTransactionInfoById.
            // For now, return the txId so callers can query the contract address.
            var resultTxId = broadcastResult.TxId ?? txId;
            return TronResult<DeployResult>.Ok(
                new DeployResult(resultTxId, string.Empty));
        }
        catch (Exception ex)
        {
            return TronResult<DeployResult>.Fail(
                TronErrorCode.ProviderConnectionFailed, ex.Message, ex.ToString());
        }
    }

    /// <summary>
    /// Deploys a standard TRC20 token using the pre-compiled template from
    /// <see cref="Trc20Template"/>. The template bytecode supports mint+burn;
    /// the <see cref="Trc20TokenOptions.Mintable"/> and
    /// <see cref="Trc20TokenOptions.Burnable"/> flags only affect the ABI
    /// returned by <see cref="Trc20Template.GetAbi"/>.
    /// </summary>
    public Task<TronResult<DeployResult>> DeployTrc20TokenAsync(
        TronAccount account, Trc20TokenOptions options, CancellationToken ct = default)
    {
        var bytecode = Trc20Template.GetBytecode(options);
        var abi = Trc20Template.GetAbi(options);
        return DeployContractAsync(account, bytecode, abi, DefaultFeeLimit, ct);
    }

    // === Contract Helpers ===

    /// <summary>
    /// Creates a <see cref="Trc20Contract"/> wrapper for interacting with an existing TRC20 token.
    /// </summary>
    public Trc20Contract GetTrc20Contract(string contractAddress, TronAccount ownerAccount)
        => new Trc20Contract(Provider, contractAddress, ownerAccount);

    // === Helpers ===

    /// <summary>
    /// Resolves a Tron address (base58 or hex) to hex format with 41 prefix.
    /// </summary>
    private static string ResolveHexAddress(string address)
    {
        if (address.StartsWith("T"))
            return TronAddress.ToHex(address);
        return address;
    }

    /// <summary>
    /// Extracts ref block bytes and hash from block info.
    /// ref_block_bytes = last 2 bytes of block number.
    /// ref_block_hash = first 8 bytes of block ID (hex string).
    /// </summary>
    private static (byte[] refBlockBytes, byte[] refBlockHash) ExtractRefBlock(BlockInfo block)
    {
        // ref_block_bytes: last 2 bytes of the block number (big-endian)
        var blockNumBytes = BitConverter.GetBytes(block.BlockNumber);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(blockNumBytes);
        var refBlockBytes = blockNumBytes[^2..];

        // ref_block_hash: first 8 bytes of the block ID
        byte[] refBlockHash;
        if (!string.IsNullOrEmpty(block.BlockId) && block.BlockId.Length >= 16)
        {
            refBlockHash = block.BlockId[..16].FromHex();
        }
        else if (block.BlockHeaderRawData.Length >= 8)
        {
            refBlockHash = block.BlockHeaderRawData[..8];
        }
        else
        {
            refBlockHash = new byte[8];
        }

        return (refBlockBytes, refBlockHash);
    }

    private static ResourceCode MapResourceType(ResourceType resource) => resource switch
    {
        ResourceType.Bandwidth => ResourceCode.Bandwidth,
        ResourceType.Energy => ResourceCode.Energy,
        _ => ResourceCode.Bandwidth
    };

    private static TronErrorCode MapBroadcastError(string? message) => message switch
    {
        not null when message.Contains("BANDWIDTH", StringComparison.OrdinalIgnoreCase)
            => TronErrorCode.InsufficientBandwidth,
        not null when message.Contains("ENERGY", StringComparison.OrdinalIgnoreCase)
            => TronErrorCode.InsufficientEnergy,
        not null when message.Contains("BALANCE", StringComparison.OrdinalIgnoreCase)
            => TronErrorCode.InsufficientBalance,
        not null when message.Contains("DUP_TRANSACTION", StringComparison.OrdinalIgnoreCase)
            => TronErrorCode.DuplicateTransaction,
        not null when message.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase)
            => TronErrorCode.TransactionExpired,
        _ => TronErrorCode.Unknown
    };

    private static TransactionStatus DetermineStatus(TransactionInfoDto txInfo, TransactionInfoDto? solidityInfo)
    {
        if (solidityInfo is null || string.IsNullOrEmpty(solidityInfo.TxId))
            return TransactionStatus.Unconfirmed;

        if (!string.IsNullOrEmpty(solidityInfo.ContractResult) &&
            solidityInfo.ContractResult.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
            return TransactionStatus.Failed;

        return TransactionStatus.Confirmed;
    }

    private static TransactionType DetermineTransactionType(TransactionInfoDto txInfo)
    {
        // In the full implementation this would parse the contract type from raw transaction data.
        // For now, return Other as we only have the DTO.
        return TransactionType.Other;
    }

    private static FailureReason ParseFailureReason(string? contractResult) => contractResult switch
    {
        not null when contractResult.Contains("OUT_OF_ENERGY", StringComparison.OrdinalIgnoreCase)
            => FailureReason.OutOfEnergy,
        not null when contractResult.Contains("OUT_OF_TIME", StringComparison.OrdinalIgnoreCase)
            => FailureReason.ContractOutOfTime,
        not null when contractResult.Contains("REVERT", StringComparison.OrdinalIgnoreCase)
            => FailureReason.ContractReverted,
        not null when contractResult.Contains("BALANCE", StringComparison.OrdinalIgnoreCase)
            => FailureReason.InsufficientBalance,
        not null when contractResult.Contains("BANDWIDTH", StringComparison.OrdinalIgnoreCase)
            => FailureReason.OutOfBandwidth,
        _ => FailureReason.Other
    };
}
