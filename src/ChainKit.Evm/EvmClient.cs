using System.Numerics;
using System.Text.Json;
using ChainKit.Core.Converters;
using ChainKit.Core.Extensions;
using ChainKit.Evm.Contracts;
using ChainKit.Evm.Crypto;
using ChainKit.Evm.Models;
using ChainKit.Evm.Protocol;
using ChainKit.Evm.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChainKit.Evm;

/// <summary>
/// High-level facade for EVM-compatible blockchain operations.
/// ERC20 operations go through Erc20Contract (via GetErc20Contract).
/// </summary>
public sealed class EvmClient : IDisposable
{
    private readonly ILogger<EvmClient> _logger;

    private static readonly TimeSpan DefaultWaitOnChainTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultWaitOnChainPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The underlying EVM provider for JSON-RPC calls.
    /// </summary>
    public IEvmProvider Provider { get; }

    /// <summary>
    /// Shared token info cache for ERC20 metadata resolution.
    /// </summary>
    public TokenInfoCache TokenCache { get; }

    /// <summary>
    /// The network configuration (chain ID, RPC URL, native currency).
    /// </summary>
    public EvmNetworkConfig Network { get; }

    /// <summary>
    /// Creates a new EvmClient instance.
    /// </summary>
    /// <param name="provider">The EVM provider (externally owned — not disposed by this client).</param>
    /// <param name="network">The network configuration.</param>
    /// <param name="logger">Optional logger. Defaults to NullLogger.</param>
    public EvmClient(IEvmProvider provider, EvmNetworkConfig network,
        ILogger<EvmClient>? logger = null)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Network = network ?? throw new ArgumentNullException(nameof(network));
        _logger = logger ?? NullLogger<EvmClient>.Instance;
        TokenCache = new TokenInfoCache(_logger);
    }

    /// <summary>
    /// Transfers native currency (ETH/POL). Amount is in ETH/POL (decimal), not Wei.
    /// </summary>
    /// <param name="from">The sender account (provides private key for signing).</param>
    /// <param name="toAddress">The recipient address (0x-prefixed).</param>
    /// <param name="amount">The amount in native currency (e.g. 1.5 ETH). Must be positive.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<EvmResult<TransferResult>> TransferAsync(
        EvmAccount from, string toAddress, decimal amount, CancellationToken ct = default)
    {
        try
        {
            if (amount <= 0)
                return EvmResult<TransferResult>.Fail(EvmErrorCode.InvalidAmount, "Amount must be positive");
            if (!EvmAddress.IsValid(toAddress))
                return EvmResult<TransferResult>.Fail(EvmErrorCode.InvalidAddress, $"Invalid address: {toAddress}");

            BigInteger weiAmount;
            try { weiAmount = TokenConverter.ToRawAmount(amount, Network.Decimals); }
            catch (OverflowException) { return EvmResult<TransferResult>.Fail(EvmErrorCode.InvalidAmount, "Amount too large"); }

            var nonce = await Provider.GetTransactionCountAsync(from.Address, ct);
            var gasLimit = await Provider.EstimateGasAsync(from.Address, toAddress, Array.Empty<byte>(), weiAmount, ct);
            var (baseFee, priorityFee) = await Provider.GetEip1559FeesAsync(ct);
            var maxFee = baseFee * 2 + priorityFee;

            var (txHash, rawTx) = EvmTransactionUtils.SignEip1559Transaction(
                Network.ChainId, nonce, priorityFee, maxFee, gasLimit,
                toAddress, weiAmount, Array.Empty<byte>(), from.PrivateKey);

            var broadcastHash = await Provider.SendRawTransactionAsync(rawTx, ct);
            return EvmResult<TransferResult>.Ok(new TransferResult(broadcastHash));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transfer failed");
            return EvmResult<TransferResult>.Fail(EvmErrorCode.Unknown, ex.Message);
        }
    }

    /// <summary>
    /// Gets the native currency balance (in ETH/POL, not Wei).
    /// </summary>
    /// <param name="address">The address to query (0x-prefixed).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<EvmResult<BalanceInfo>> GetBalanceAsync(string address, CancellationToken ct = default)
    {
        try
        {
            if (!EvmAddress.IsValid(address))
                return EvmResult<BalanceInfo>.Fail(EvmErrorCode.InvalidAddress, $"Invalid address: {address}");

            var weiBalance = await Provider.GetBalanceAsync(address, ct);
            var balance = TokenConverter.ToTokenAmount(weiBalance, Network.Decimals);
            return EvmResult<BalanceInfo>.Ok(new BalanceInfo(balance, weiBalance));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetBalance failed for {Address}", address);
            return EvmResult<BalanceInfo>.Fail(EvmErrorCode.ProviderConnectionFailed, ex.Message);
        }
    }

    /// <summary>
    /// Gets transaction detail by hash. Merges tx data + receipt into a unified view.
    /// No receipt = Unconfirmed, receipt status 0x1 = Confirmed, 0x0 = Failed.
    /// </summary>
    /// <param name="txHash">The transaction hash (0x-prefixed).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<EvmResult<EvmTransactionDetail>> GetTransactionDetailAsync(
        string txHash, CancellationToken ct = default)
    {
        try
        {
            var txData = await Provider.GetTransactionByHashAsync(txHash, ct);
            if (txData == null)
                return EvmResult<EvmTransactionDetail>.Fail(EvmErrorCode.TransactionNotFound, $"Transaction not found: {txHash}");

            var receipt = await Provider.GetTransactionReceiptAsync(txHash, ct);
            var detail = BuildTransactionDetail(txHash, txData.Value, receipt);
            return EvmResult<EvmTransactionDetail>.Ok(detail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTransactionDetail failed for {TxHash}", txHash);
            return EvmResult<EvmTransactionDetail>.Fail(EvmErrorCode.Unknown, ex.Message);
        }
    }

    /// <summary>
    /// Creates an Erc20Contract instance for interacting with an ERC20 token.
    /// </summary>
    /// <param name="contractAddress">The ERC-20 contract address (0x-prefixed).</param>
    public Erc20Contract GetErc20Contract(string contractAddress)
        => new(Provider, contractAddress, Network, TokenCache);

    /// <summary>
    /// Gets the latest block number.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task<EvmResult<long>> GetBlockNumberAsync(CancellationToken ct = default)
    {
        try
        {
            var blockNumber = await Provider.GetBlockNumberAsync(ct);
            return EvmResult<long>.Ok(blockNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetBlockNumber failed");
            return EvmResult<long>.Fail(EvmErrorCode.ProviderConnectionFailed, ex.Message);
        }
    }

    /// <summary>
    /// Polls until the transaction has a receipt (mined into a block).
    /// Lightweight variant: returns only the raw receipt JSON, skipping eth_getTransactionByHash.
    /// Use when you only need to confirm inclusion and don't need the full merged detail.
    /// </summary>
    /// <param name="txHash">The transaction hash returned by the broadcast call.</param>
    /// <param name="timeout">Total time to wait. Defaults to 60 seconds.</param>
    /// <param name="pollInterval">Interval between polls. Defaults to 2 seconds.</param>
    /// <param name="maxConsecutiveFailures">
    /// Number of consecutive provider exceptions before giving up. Set to 0 to retry indefinitely
    /// until timeout. Defaults to 5.
    /// </param>
    /// <param name="ct">Cancellation token. Cancellation throws OperationCanceledException.</param>
    public async Task<EvmResult<JsonElement>> WaitForReceiptAsync(
        string txHash,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        int maxConsecutiveFailures = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(txHash))
            return EvmResult<JsonElement>.Fail(EvmErrorCode.InvalidArgument, "txHash must not be null or empty");
        if (maxConsecutiveFailures < 0)
            return EvmResult<JsonElement>.Fail(EvmErrorCode.InvalidArgument, "maxConsecutiveFailures must be >= 0");

        var effectiveTimeout = timeout ?? DefaultWaitOnChainTimeout;
        if (effectiveTimeout < TimeSpan.Zero)
            return EvmResult<JsonElement>.Fail(EvmErrorCode.InvalidArgument, "timeout must be >= zero");

        var effectivePollInterval = pollInterval ?? DefaultWaitOnChainPollInterval;
        if (effectivePollInterval <= TimeSpan.Zero)
            return EvmResult<JsonElement>.Fail(EvmErrorCode.InvalidArgument, "pollInterval must be > zero");

        var deadline = DateTime.UtcNow + effectiveTimeout;
        var failures = 0;
        string? lastFailureMessage = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var receipt = await Provider.GetTransactionReceiptAsync(txHash, ct);
                failures = 0;
                if (receipt is not null)
                    return EvmResult<JsonElement>.Ok(receipt.Value);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures++;
                lastFailureMessage = ex.Message;
                _logger.LogWarning(ex, "WaitForReceiptAsync: provider call failed (attempt {Failures})", failures);
                if (maxConsecutiveFailures > 0 && failures >= maxConsecutiveFailures)
                    return EvmResult<JsonElement>.Fail(EvmErrorCode.ProviderConnectionFailed, ex.Message);
            }

            if (DateTime.UtcNow >= deadline)
            {
                var msg = lastFailureMessage is null
                    ? $"Transaction {txHash} has no receipt within {effectiveTimeout}"
                    : $"Transaction {txHash} has no receipt within {effectiveTimeout} (last error: {lastFailureMessage})";
                return EvmResult<JsonElement>.Fail(EvmErrorCode.ProviderTimeout, msg);
            }

            await Task.Delay(effectivePollInterval, ct);
        }
    }

    /// <summary>
    /// Polls until the transaction is mined and returns the merged tx + receipt detail.
    /// Use after broadcast when a follow-up tx depends on this tx's effects.
    /// One additional eth_getTransactionByHash call is made when the receipt appears,
    /// to populate sender/recipient/value/nonce. Use WaitForReceiptAsync if you don't need them.
    /// </summary>
    /// <param name="txHash">The transaction hash returned by the broadcast call.</param>
    /// <param name="timeout">Total time to wait. Defaults to 60 seconds.</param>
    /// <param name="pollInterval">Interval between polls. Defaults to 2 seconds.</param>
    /// <param name="maxConsecutiveFailures">
    /// Number of consecutive provider exceptions before giving up. Set to 0 to retry indefinitely
    /// until timeout. Defaults to 5.
    /// </param>
    /// <param name="ct">Cancellation token. Cancellation throws OperationCanceledException.</param>
    public async Task<EvmResult<EvmTransactionDetail>> WaitForOnChainAsync(
        string txHash,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        int maxConsecutiveFailures = 5,
        CancellationToken ct = default)
    {
        var receiptResult = await WaitForReceiptAsync(txHash, timeout, pollInterval, maxConsecutiveFailures, ct);
        if (!receiptResult.Success)
        {
            var code = receiptResult.ErrorCode ?? EvmErrorCode.Unknown;
            var message = receiptResult.Error?.Message ?? "Wait for receipt failed";
            return EvmResult<EvmTransactionDetail>.Fail(code, message);
        }

        try
        {
            var txData = await Provider.GetTransactionByHashAsync(txHash, ct);
            if (txData is null)
                return EvmResult<EvmTransactionDetail>.Fail(
                    EvmErrorCode.TransactionNotFound,
                    $"Receipt found but eth_getTransactionByHash returned null for {txHash}");

            var detail = BuildTransactionDetail(txHash, txData.Value, receiptResult.Data);
            return EvmResult<EvmTransactionDetail>.Ok(detail);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WaitForOnChainAsync: post-receipt tx fetch failed for {TxHash}", txHash);
            return EvmResult<EvmTransactionDetail>.Fail(EvmErrorCode.ProviderConnectionFailed, ex.Message);
        }
    }

    private EvmTransactionDetail BuildTransactionDetail(string txHash, JsonElement txData, JsonElement? receipt)
    {
        // Parse fields from txData
        var from = GetHexString(txData, "from");
        var to = GetHexString(txData, "to");
        var nonce = ParseHexLong(txData, "nonce");
        var valueWei = ParseHexBigInteger(txData, "value");

        // Convert value from Wei to native currency
        var amount = TokenConverter.ToTokenAmount(valueWei, Network.Decimals);

        // Determine status and parse receipt fields
        TransactionStatus status;
        long gasUsed = 0;
        BigInteger effectiveGasPrice = BigInteger.Zero;
        decimal fee = 0m;
        long blockNumber = 0;
        FailureInfo? failure = null;

        if (receipt == null)
        {
            status = TransactionStatus.Unconfirmed;
            // Try to get blockNumber from txData (pending tx may not have it)
            blockNumber = ParseHexLong(txData, "blockNumber");
        }
        else
        {
            var receiptElement = receipt.Value;
            var statusHex = GetHexString(receiptElement, "status");
            status = statusHex == "0x1" ? TransactionStatus.Confirmed : TransactionStatus.Failed;

            gasUsed = ParseHexLong(receiptElement, "gasUsed");
            effectiveGasPrice = ParseHexBigInteger(receiptElement, "effectiveGasPrice");
            blockNumber = ParseHexLong(receiptElement, "blockNumber");

            // Fee = gasUsed * effectiveGasPrice (in Wei), convert to native currency
            var feeWei = new BigInteger(gasUsed) * effectiveGasPrice;
            fee = TokenConverter.ToTokenAmount(feeWei, Network.Decimals);

            if (status == TransactionStatus.Failed)
            {
                failure = new FailureInfo("Transaction reverted", null);
            }
        }

        return new EvmTransactionDetail
        {
            TxId = txHash,
            FromAddress = from,
            ToAddress = to,
            Amount = amount,
            Status = status,
            BlockNumber = blockNumber,
            Nonce = nonce,
            GasUsed = gasUsed,
            GasPrice = effectiveGasPrice,
            Fee = fee,
            Failure = failure
        };
    }

    private static string GetHexString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString() ?? "";
        return "";
    }

    private static long ParseHexLong(JsonElement element, string propertyName)
    {
        var hex = GetHexString(element, propertyName);
        if (string.IsNullOrEmpty(hex)) return 0;
        hex = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        if (string.IsNullOrEmpty(hex)) return 0;
        return long.Parse(hex, System.Globalization.NumberStyles.HexNumber);
    }

    private static BigInteger ParseHexBigInteger(JsonElement element, string propertyName)
    {
        var hex = GetHexString(element, propertyName);
        if (string.IsNullOrEmpty(hex)) return BigInteger.Zero;
        hex = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        if (string.IsNullOrEmpty(hex)) return BigInteger.Zero;
        // Pad with leading zero to ensure unsigned interpretation
        var bytes = Convert.FromHexString(hex.Length % 2 == 1 ? "0" + hex : hex);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
    }

    /// <summary>
    /// Disposes the client. The provider is externally owned and is NOT disposed.
    /// </summary>
    public void Dispose() { /* Provider is externally owned */ }
}
