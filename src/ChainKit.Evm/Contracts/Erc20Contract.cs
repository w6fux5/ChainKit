using System.Numerics;
using ChainKit.Core.Crypto;
using ChainKit.Core.Extensions;
using ChainKit.Evm.Crypto;
using ChainKit.Evm.Models;
using ChainKit.Evm.Protocol;
using ChainKit.Evm.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChainKit.Evm.Contracts;

/// <summary>
/// ERC-20 contract interaction facade. Provides read-only queries (name, symbol, decimals,
/// totalSupply, balanceOf, allowance) and write operations (transfer, approve) using the Result pattern.
/// Thread-safe: decimals resolution is protected by SemaphoreSlim.
/// </summary>
public sealed class Erc20Contract : IDisposable
{
    private readonly IEvmProvider _provider;
    private readonly string _contractAddress;
    private readonly EvmNetworkConfig _network;
    private readonly TokenInfoCache? _tokenCache;
    private readonly ILogger<Erc20Contract> _logger;
    private readonly SemaphoreSlim _decimalsLock = new(1, 1);
    private int? _cachedDecimals;

    /// <summary>
    /// The ERC-20 contract address.
    /// </summary>
    public string ContractAddress => _contractAddress;

    /// <summary>
    /// Creates a new Erc20Contract instance.
    /// </summary>
    /// <param name="provider">The EVM provider for RPC calls.</param>
    /// <param name="contractAddress">The ERC-20 contract address (0x-prefixed).</param>
    /// <param name="network">The network configuration (for chain ID in signing).</param>
    /// <param name="tokenCache">Optional token info cache for metadata lookup.</param>
    /// <param name="logger">Optional logger. Defaults to NullLogger.</param>
    public Erc20Contract(IEvmProvider provider, string contractAddress,
        EvmNetworkConfig network, TokenInfoCache? tokenCache = null,
        ILogger<Erc20Contract>? logger = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _contractAddress = contractAddress ?? throw new ArgumentNullException(nameof(contractAddress));
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _tokenCache = tokenCache;
        _logger = logger ?? NullLogger<Erc20Contract>.Instance;
    }

    /// <summary>
    /// Queries the token name via eth_call.
    /// </summary>
    public async Task<EvmResult<string>> NameAsync(CancellationToken ct = default)
    {
        try
        {
            var data = AbiEncoder.EncodeFunctionSelector("name()");
            var result = await _provider.CallAsync(_contractAddress, data, ct);
            var decoded = AbiEncoder.DecodeString(result.FromHex());
            return EvmResult<string>.Ok(decoded);
        }
        catch (Exception ex) { return EvmResult<string>.Fail(EvmErrorCode.ContractReverted, ex.Message); }
    }

    /// <summary>
    /// Queries the token symbol via eth_call.
    /// </summary>
    public async Task<EvmResult<string>> SymbolAsync(CancellationToken ct = default)
    {
        try
        {
            var data = AbiEncoder.EncodeFunctionSelector("symbol()");
            var result = await _provider.CallAsync(_contractAddress, data, ct);
            var decoded = AbiEncoder.DecodeString(result.FromHex());
            return EvmResult<string>.Ok(decoded);
        }
        catch (Exception ex) { return EvmResult<string>.Fail(EvmErrorCode.ContractReverted, ex.Message); }
    }

    /// <summary>
    /// Queries the token decimals via eth_call. Result is cached after first successful call.
    /// </summary>
    public async Task<EvmResult<int>> DecimalsAsync(CancellationToken ct = default)
    {
        try
        {
            var d = await GetDecimalsInternalAsync(ct);
            return EvmResult<int>.Ok(d);
        }
        catch (Exception ex) { return EvmResult<int>.Fail(EvmErrorCode.ContractReverted, ex.Message); }
    }

    /// <summary>
    /// Queries the token total supply via eth_call.
    /// </summary>
    public async Task<EvmResult<BigInteger>> TotalSupplyAsync(CancellationToken ct = default)
    {
        try
        {
            var data = AbiEncoder.EncodeFunctionSelector("totalSupply()");
            var result = await _provider.CallAsync(_contractAddress, data, ct);
            return EvmResult<BigInteger>.Ok(AbiEncoder.DecodeUint256(result.FromHex()));
        }
        catch (Exception ex) { return EvmResult<BigInteger>.Fail(EvmErrorCode.ContractReverted, ex.Message); }
    }

    /// <summary>
    /// Queries the ERC-20 balance of an address via eth_call.
    /// </summary>
    /// <param name="address">The address to query (0x-prefixed).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<EvmResult<BigInteger>> BalanceOfAsync(string address, CancellationToken ct = default)
    {
        try
        {
            var data = EvmAbiEncoder.EncodeBalanceOf(address);
            var result = await _provider.CallAsync(_contractAddress, data, ct);
            return EvmResult<BigInteger>.Ok(AbiEncoder.DecodeUint256(result.FromHex()));
        }
        catch (Exception ex) { return EvmResult<BigInteger>.Fail(EvmErrorCode.ContractReverted, ex.Message); }
    }

    /// <summary>
    /// Queries the ERC-20 allowance granted by owner to spender via eth_call.
    /// </summary>
    /// <param name="owner">The token owner address.</param>
    /// <param name="spender">The spender address.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<EvmResult<BigInteger>> AllowanceAsync(string owner, string spender, CancellationToken ct = default)
    {
        try
        {
            var data = EvmAbiEncoder.EncodeAllowance(owner, spender);
            var result = await _provider.CallAsync(_contractAddress, data, ct);
            return EvmResult<BigInteger>.Ok(AbiEncoder.DecodeUint256(result.FromHex()));
        }
        catch (Exception ex) { return EvmResult<BigInteger>.Fail(EvmErrorCode.ContractReverted, ex.Message); }
    }

    /// <summary>
    /// Queries all token metadata (name, symbol, decimals, totalSupply) in parallel.
    /// </summary>
    public async Task<EvmResult<TokenInfo>> GetTokenInfoAsync(CancellationToken ct = default)
    {
        try
        {
            var nameTask = NameAsync(ct);
            var symbolTask = SymbolAsync(ct);
            var decimalsTask = DecimalsAsync(ct);
            var totalSupplyTask = TotalSupplyAsync(ct);
            await Task.WhenAll(nameTask, symbolTask, decimalsTask, totalSupplyTask);

            var name = nameTask.Result.Success ? nameTask.Result.Data! : "";
            var symbol = symbolTask.Result.Success ? symbolTask.Result.Data! : "";
            var decimals = decimalsTask.Result.Success ? decimalsTask.Result.Data : 0;
            var totalSupply = totalSupplyTask.Result.Success ? totalSupplyTask.Result.Data : BigInteger.Zero;

            var info = new TokenInfo(_contractAddress, name, symbol, decimals, totalSupply, null);
            return EvmResult<TokenInfo>.Ok(info);
        }
        catch (Exception ex) { return EvmResult<TokenInfo>.Fail(EvmErrorCode.ContractReverted, ex.Message); }
    }

    /// <summary>
    /// Executes an ERC-20 transfer: ABI encode, estimate gas, get fees, build and sign EIP-1559 tx, broadcast.
    /// </summary>
    /// <param name="from">The sender account (provides private key for signing).</param>
    /// <param name="toAddress">The recipient address.</param>
    /// <param name="rawAmount">The token amount in raw units (no decimals applied).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<EvmResult<TransferResult>> TransferAsync(
        EvmAccount from, string toAddress, BigInteger rawAmount, CancellationToken ct = default)
    {
        try
        {
            var data = EvmAbiEncoder.EncodeTransfer(toAddress, rawAmount);
            return await ExecuteWriteAsync(from, data, ct);
        }
        catch (Exception ex) { return EvmResult<TransferResult>.Fail(EvmErrorCode.ContractReverted, ex.Message); }
    }

    /// <summary>
    /// Executes an ERC-20 approve: grants spender allowance to spend tokens on behalf of the sender.
    /// </summary>
    /// <param name="from">The token owner account.</param>
    /// <param name="spenderAddress">The spender address to approve.</param>
    /// <param name="rawAmount">The allowance amount in raw units.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<EvmResult<TransferResult>> ApproveAsync(
        EvmAccount from, string spenderAddress, BigInteger rawAmount, CancellationToken ct = default)
    {
        try
        {
            var data = EvmAbiEncoder.EncodeApprove(spenderAddress, rawAmount);
            return await ExecuteWriteAsync(from, data, ct);
        }
        catch (Exception ex) { return EvmResult<TransferResult>.Fail(EvmErrorCode.ContractReverted, ex.Message); }
    }

    private async Task<EvmResult<TransferResult>> ExecuteWriteAsync(
        EvmAccount from, byte[] callData, CancellationToken ct)
    {
        var nonce = await _provider.GetTransactionCountAsync(from.Address, ct);
        var gasLimit = await _provider.EstimateGasAsync(from.Address, _contractAddress, callData, null, ct);
        var (baseFee, priorityFee) = await _provider.GetEip1559FeesAsync(ct);
        var maxFee = baseFee * 2 + priorityFee;

        var (txHash, rawTx) = EvmTransactionUtils.SignEip1559Transaction(
            _network.ChainId, nonce, priorityFee, maxFee, gasLimit,
            _contractAddress, BigInteger.Zero, callData, from.PrivateKey);

        var broadcastHash = await _provider.SendRawTransactionAsync(rawTx, ct);
        return EvmResult<TransferResult>.Ok(new TransferResult(broadcastHash));
    }

    private async Task<int> GetDecimalsInternalAsync(CancellationToken ct)
    {
        if (_cachedDecimals.HasValue) return _cachedDecimals.Value;
        await _decimalsLock.WaitAsync(ct);
        try
        {
            if (_cachedDecimals.HasValue) return _cachedDecimals.Value;
            var data = AbiEncoder.EncodeFunctionSelector("decimals()");
            var result = await _provider.CallAsync(_contractAddress, data, ct);
            _cachedDecimals = (int)AbiEncoder.DecodeUint256(result.FromHex());
            return _cachedDecimals.Value;
        }
        finally { _decimalsLock.Release(); }
    }

    /// <summary>
    /// Disposes the internal SemaphoreSlim.
    /// </summary>
    public void Dispose() => _decimalsLock.Dispose();
}
