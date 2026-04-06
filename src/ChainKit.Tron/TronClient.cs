using System.Numerics;
using ChainKit.Core.Extensions;
using ChainKit.Tron.Contracts;
using ChainKit.Tron.Crypto;
using ChainKit.Tron.Models;
using ChainKit.Tron.Protocol;
using ChainKit.Tron.Protocol.Protobuf;
using ChainKit.Tron.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChainKit.Tron;

/// <summary>
/// High-level facade that integrates multiple low-level provider calls into single operations.
/// All public methods return <see cref="TronResult{T}"/>.
/// </summary>
public class TronClient : IDisposable
{
    private const long SunPerTrx = 1_000_000;
    private const long DefaultFeeLimit = 100_000_000; // 100 TRX
    private readonly ILogger _logger;

    public ITronProvider Provider { get; }
    internal TokenInfoCache TokenCache { get; }

    public TronClient(ITronProvider provider, ILogger<TronClient>? logger = null)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _logger = logger ?? NullLogger<TronClient>.Instance;
        TokenCache = new TokenInfoCache(_logger);
    }

    // === Transfer ===

    /// <summary>
    /// Transfers TRX from one account to another.
    /// Flow: getNowBlock -> build tx with ref block -> sign -> broadcast
    /// </summary>
    public async Task<TronResult<TransferResult>> TransferTrxAsync(
        TronAccount from, string toAddress, decimal trxAmount, CancellationToken ct = default)
    {
        if (trxAmount <= 0)
            return TronResult<TransferResult>.Fail(TronErrorCode.InvalidAmount, "Amount must be positive");

        try
        {
            long amountSun;
            try { amountSun = checked((long)(trxAmount * SunPerTrx)); }
            catch (OverflowException) { return TronResult<TransferResult>.Fail(TronErrorCode.InvalidAmount, "Amount too large"); }
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
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Solidity info unavailable for tx {TxId}, treating as unconfirmed", txId);
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
                var failureSource = solidityInfo?.ReceiptResult ?? txInfo.ContractResult ?? "Unknown failure";
                failure = new FailureInfo(
                    ParseFailureReason(failureSource),
                    failureSource,
                    null, txInfo.ContractResult);
            }

            var cost = solidityInfo is not null
                ? new ResourceCost(
                    (decimal)solidityInfo.Fee / SunPerTrx,
                    solidityInfo.NetUsage,
                    solidityInfo.EnergyUsage,
                    (decimal)solidityInfo.NetFee / SunPerTrx,
                    (decimal)solidityInfo.EnergyFee / SunPerTrx)
                : null;

            // Resolve from/to addresses (convert hex 41-prefix to base58 if present)
            var fromAddress = FormatAddress(txInfo.OwnerAddress);
            var toAddress = type == TransactionType.Trc20Transfer || type == TransactionType.ContractCall
                ? FormatAddress(txInfo.ContractAddress ?? txInfo.ToAddress)
                : FormatAddress(txInfo.ToAddress);

            // Resolve amount and token transfer info
            decimal amount = 0m;
            TokenTransferInfo? tokenTransfer = null;

            if (type == TransactionType.NativeTransfer)
            {
                amount = (decimal)txInfo.AmountSun / SunPerTrx;
            }
            else if (type == TransactionType.Trc20Transfer && txInfo.ContractData is not null && txInfo.ContractData.Length >= 72)
            {
                // Decode TRC20 transfer: selector(8) + address(64) + amount(64)
                var data = txInfo.ContractData;
                var recipientHex = "41" + data.Substring(8 + 24, 40); // strip 12 bytes zero-padding from 32-byte address
                var amountHex = data.Substring(72); // remaining 64 hex chars = uint256
                var rawAmount = amountHex.Length > 0
                    ? new System.Numerics.BigInteger(Convert.FromHexString(amountHex.PadLeft(64, '0')), isUnsigned: true, isBigEndian: true)
                    : System.Numerics.BigInteger.Zero;

                toAddress = FormatAddress(recipientHex);

                // Resolve token symbol + decimals via three-layer cache
                var contractAddr = txInfo.ContractAddress ?? "";
                var tokenInfo = await TokenCache.GetOrResolveAsync(contractAddr, Provider, ct);
                var rawAmountDecimal = (decimal)rawAmount;
                decimal? convertedAmount = tokenInfo.Decimals > 0
                    ? rawAmountDecimal / TronConverter.DecimalPow10(tokenInfo.Decimals)
                    : null;
                amount = convertedAmount ?? rawAmountDecimal;

                tokenTransfer = new TokenTransferInfo(
                    ContractAddress: FormatAddress(contractAddr),
                    Symbol: tokenInfo.Symbol,
                    Decimals: tokenInfo.Decimals,
                    RawAmount: rawAmountDecimal,
                    Amount: convertedAmount);
            }
            else if (type == TransactionType.Stake || type == TransactionType.Unstake
                     || type == TransactionType.Delegate || type == TransactionType.Undelegate)
            {
                amount = (decimal)txInfo.AmountSun / SunPerTrx;
            }

            var detail = new TronTransactionDetail(
                TxId: txId,
                FromAddress: fromAddress,
                ToAddress: toAddress,
                Status: status,
                Failure: failure,
                Type: type,
                Amount: amount,
                TokenTransfer: tokenTransfer,
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

            var trc20Balances = new Dictionary<string, Trc20BalanceInfo>();

            foreach (var contract in trc20Contracts)
            {
                try
                {
                    var contractHex = ResolveHexAddress(contract);
                    var data = AbiEncoder.EncodeBalanceOf(hexAddress);
                    var result = await Provider.TriggerConstantContractAsync(
                        hexAddress, contractHex, "balanceOf(address)", data[4..], ct);

                    var rawBalance = result.Length >= 32
                        ? AbiEncoder.DecodeUint256(result)
                        : BigInteger.Zero;

                    // Resolve decimals via three-layer cache and convert
                    var tokenInfo = await TokenCache.GetOrResolveAsync(contractHex, Provider, ct);
                    var rawBalanceDecimal = (decimal)rawBalance;
                    decimal? convertedBalance = tokenInfo.Decimals > 0
                        ? rawBalanceDecimal / TronConverter.DecimalPow10(tokenInfo.Decimals)
                        : null;
                    trc20Balances[contract] = new Trc20BalanceInfo(rawBalanceDecimal, convertedBalance, tokenInfo.Symbol, tokenInfo.Decimals);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "TRC20 balance query failed for contract {Contract}, reporting zero", contract);
                    trc20Balances[contract] = new Trc20BalanceInfo(0m, 0m, "", 0);
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

    /// <summary>
    /// Gets a complete account overview: TRX balance, resource usage, and recent transactions.
    /// </summary>

    // === Resource Management (Staking 2.0) ===

    /// <summary>
    /// Stakes TRX for bandwidth or energy (Stake 2.0).
    /// </summary>
    public async Task<TronResult<StakeResult>> StakeTrxAsync(
        TronAccount account, decimal trxAmount, ResourceType resource, CancellationToken ct = default)
    {
        if (trxAmount <= 0)
            return TronResult<StakeResult>.Fail(TronErrorCode.InvalidAmount, "Amount must be positive");

        try
        {
            long amountSun;
            try { amountSun = checked((long)(trxAmount * SunPerTrx)); }
            catch (OverflowException) { return TronResult<StakeResult>.Fail(TronErrorCode.InvalidAmount, "Amount too large"); }
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
        if (trxAmount <= 0)
            return TronResult<UnstakeResult>.Fail(TronErrorCode.InvalidAmount, "Amount must be positive");

        try
        {
            long amountSun;
            try { amountSun = checked((long)(trxAmount * SunPerTrx)); }
            catch (OverflowException) { return TronResult<UnstakeResult>.Fail(TronErrorCode.InvalidAmount, "Amount too large"); }
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
        if (trxAmount <= 0)
            return TronResult<DelegateResult>.Fail(TronErrorCode.InvalidAmount, "Amount must be positive");

        try
        {
            long amountSun;
            try { amountSun = checked((long)(trxAmount * SunPerTrx)); }
            catch (OverflowException) { return TronResult<DelegateResult>.Fail(TronErrorCode.InvalidAmount, "Amount too large"); }
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
        if (trxAmount <= 0)
            return TronResult<UndelegateResult>.Fail(TronErrorCode.InvalidAmount, "Amount must be positive");

        try
        {
            long amountSun;
            try { amountSun = checked((long)(trxAmount * SunPerTrx)); }
            catch (OverflowException) { return TronResult<UndelegateResult>.Fail(TronErrorCode.InvalidAmount, "Amount too large"); }
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
    /// Merges data from GetAccountAsync (staking amounts from frozenV2),
    /// GetAccountResourceAsync (bandwidth/energy limits and usage), and
    /// delegation queries via GetDelegatedResourceAccountIndexAsync / GetDelegatedResourceAsync.
    /// Delegation queries are best-effort: if they fail, the rest of the resource info
    /// is still returned with empty delegation lists.
    /// </summary>
    public async Task<TronResult<ResourceInfo>> GetResourceInfoAsync(
        string address, CancellationToken ct = default)
    {
        try
        {
            var hexAddress = ResolveHexAddress(address);

            // Fetch account info (staking data) and resource info (bandwidth/energy) in parallel
            var accountTask = Provider.GetAccountAsync(hexAddress, ct);
            var resourceTask = Provider.GetAccountResourceAsync(hexAddress, ct);
            await Task.WhenAll(accountTask, resourceTask);

            var accountInfo = accountTask.Result;
            var resourceInfo = resourceTask.Result;

            // Fetch delegation info (best-effort)
            IReadOnlyList<DelegationInfo> delegationsOut = Array.Empty<DelegationInfo>();
            IReadOnlyList<DelegationInfo> delegationsIn = Array.Empty<DelegationInfo>();

            try
            {
                var index = await Provider.GetDelegatedResourceAccountIndexAsync(hexAddress, ct);

                var outList = new List<DelegationInfo>();
                var inList = new List<DelegationInfo>();

                // Query delegations OUT (this account delegated TO these addresses)
                foreach (var toAddr in index.ToAddresses)
                {
                    try
                    {
                        var resources = await Provider.GetDelegatedResourceAsync(hexAddress, toAddr, ct);
                        foreach (var r in resources)
                        {
                            if (r.FrozenBalanceForBandwidth > 0)
                            {
                                outList.Add(new DelegationInfo(
                                    FormatAddress(toAddr),
                                    (decimal)r.FrozenBalanceForBandwidth / SunPerTrx,
                                    ResourceType.Bandwidth, false));
                            }
                            if (r.FrozenBalanceForEnergy > 0)
                            {
                                outList.Add(new DelegationInfo(
                                    FormatAddress(toAddr),
                                    (decimal)r.FrozenBalanceForEnergy / SunPerTrx,
                                    ResourceType.Energy, false));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Delegation query failed for {From} -> {To}", hexAddress, toAddr);
                    }
                }

                // Query delegations IN (these addresses delegated TO this account)
                foreach (var fromAddr in index.FromAddresses)
                {
                    try
                    {
                        var resources = await Provider.GetDelegatedResourceAsync(fromAddr, hexAddress, ct);
                        foreach (var r in resources)
                        {
                            if (r.FrozenBalanceForBandwidth > 0)
                            {
                                inList.Add(new DelegationInfo(
                                    FormatAddress(fromAddr),
                                    (decimal)r.FrozenBalanceForBandwidth / SunPerTrx,
                                    ResourceType.Bandwidth, false));
                            }
                            if (r.FrozenBalanceForEnergy > 0)
                            {
                                inList.Add(new DelegationInfo(
                                    FormatAddress(fromAddr),
                                    (decimal)r.FrozenBalanceForEnergy / SunPerTrx,
                                    ResourceType.Energy, false));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Delegation query failed for {From} -> {To}", fromAddr, hexAddress);
                    }
                }

                delegationsOut = outList;
                delegationsIn = inList;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Delegation index query failed for {Address}", hexAddress);
            }

            return TronResult<ResourceInfo>.Ok(new ResourceInfo(
                BandwidthTotal: resourceInfo.FreeBandwidthLimit + resourceInfo.TotalBandwidthLimit,
                BandwidthUsed: resourceInfo.FreeBandwidthUsed + resourceInfo.TotalBandwidthUsed,
                EnergyTotal: resourceInfo.EnergyLimit,
                EnergyUsed: resourceInfo.EnergyUsed,
                StakedForBandwidth: (decimal)accountInfo.FrozenBalanceForBandwidth / SunPerTrx,
                StakedForEnergy: (decimal)accountInfo.FrozenBalanceForEnergy / SunPerTrx,
                DelegationsOut: delegationsOut,
                DelegationsIn: delegationsIn));
        }
        catch (Exception ex)
        {
            return TronResult<ResourceInfo>.Fail(
                TronErrorCode.ProviderConnectionFailed, ex.Message, ex.ToString());
        }
    }

    // === Resource Exchange Rate ===

    /// <summary>
    /// Gets the current exchange rate between TRX and Energy/Bandwidth.
    /// Supports bidirectional conversion via <see cref="ResourceExchangeRate.EstimateResource"/>
    /// and <see cref="ResourceExchangeRate.EstimateTrx"/>.
    /// </summary>
    public async Task<TronResult<ResourceExchangeRate>> GetResourceExchangeRateAsync(
        ResourceType resource, CancellationToken ct = default)
    {
        try
        {
            // Use a zero-prefix address to query network-wide resource data
            var resourceInfo = await Provider.GetAccountResourceAsync(
                "410000000000000000000000000000000000000000", ct);

            var totalStaked = resource == ResourceType.Energy
                ? resourceInfo.NetworkTotalEnergyWeight
                : resourceInfo.NetworkTotalBandwidthWeight;
            var totalLimit = resource == ResourceType.Energy
                ? resourceInfo.NetworkTotalEnergyLimit
                : resourceInfo.NetworkTotalBandwidthLimit;

            if (totalStaked <= 0 || totalLimit <= 0)
            {
                return TronResult<ResourceExchangeRate>.Fail(
                    TronErrorCode.Unknown, "Network resource data unavailable");
            }

            // TotalEnergyWeight / TotalNetWeight 單位是 TRX（非 Sun）
            // Formula: resource = (myTrx / totalStakedTrx) × totalLimit
            // So: resourcePerTrx = totalLimit / totalStakedTrx
            var resourcePerTrx = (decimal)totalLimit / totalStaked;
            var trxPerResource = (decimal)totalStaked / totalLimit;
            var totalStakedTrx = (decimal)totalStaked;

            return TronResult<ResourceExchangeRate>.Ok(new ResourceExchangeRate(
                resource, resourcePerTrx, trxPerResource, totalStakedTrx, totalLimit));
        }
        catch (Exception ex)
        {
            return TronResult<ResourceExchangeRate>.Fail(
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

    // === IDisposable ===

    public void Dispose()
    {
        if (Provider is IDisposable d)
            d.Dispose();
        GC.SuppressFinalize(this);
    }

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
    /// Formats an address for display. If the address is a valid hex address
    /// (starts with 41 and is 42 hex chars), converts to base58. Otherwise returns as-is.
    /// Returns empty string for null/empty input.
    /// </summary>
    private static string FormatAddress(string? address)
    {
        if (string.IsNullOrEmpty(address))
            return string.Empty;

        // Convert hex addresses (41-prefix, 42 hex chars = 21 bytes) to base58
        if (address.Length == 42
            && address.StartsWith("41", StringComparison.OrdinalIgnoreCase)
            && address.All(c => char.IsAsciiHexDigit(c)))
        {
            try
            {
                return TronAddress.ToBase58(address);
            }
            catch
            {
                return address;
            }
        }

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

        // ref_block_hash: bytes 8-15 of the block ID (hex positions 16..32).
        // The Tron TAPOS mechanism uses the middle 8 bytes of the 32-byte block hash,
        // NOT the first 8 bytes.
        byte[] refBlockHash;
        if (!string.IsNullOrEmpty(block.BlockId) && block.BlockId.Length >= 32)
        {
            refBlockHash = block.BlockId[16..32].FromHex();
        }
        else if (block.BlockHeaderRawData.Length >= 16)
        {
            refBlockHash = block.BlockHeaderRawData[8..16];
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

        // Smart Contract 交易：receipt.result 必須為 SUCCESS 才算確認成功
        // REVERT、OUT_OF_ENERGY 等都是失敗
        if (!string.IsNullOrEmpty(solidityInfo.ReceiptResult) &&
            !solidityInfo.ReceiptResult.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase))
            return TransactionStatus.Failed;

        return TransactionStatus.Confirmed;
    }

    private static TransactionType DetermineTransactionType(TransactionInfoDto txInfo) => txInfo.ContractType switch
    {
        "TransferContract" => TransactionType.NativeTransfer,
        "TransferAssetContract" => TransactionType.Trc10Transfer,
        "TriggerSmartContract" => ClassifySmartContractCall(txInfo.ContractData),
        "CreateSmartContract" => TransactionType.ContractDeploy,
        "FreezeBalanceContract" or "FreezeBalanceV2Contract" => TransactionType.Stake,
        "UnfreezeBalanceContract" or "UnfreezeBalanceV2Contract" => TransactionType.Unstake,
        "DelegateResourceContract" => TransactionType.Delegate,
        "UnDelegateResourceContract" => TransactionType.Undelegate,
        _ => TransactionType.Other
    };

    /// <summary>
    /// Classifies a TriggerSmartContract call based on the ABI data prefix.
    /// The selector <c>a9059cbb</c> is the keccak256 hash of <c>transfer(address,uint256)</c>.
    /// </summary>
    private static TransactionType ClassifySmartContractCall(string? contractData)
    {
        if (contractData is not null && contractData.Length >= 8)
        {
            var selector = contractData[..8].ToLowerInvariant();
            if (selector == "a9059cbb") // transfer(address,uint256)
                return TransactionType.Trc20Transfer;
        }
        return TransactionType.ContractCall;
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
