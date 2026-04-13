using System.Numerics;
using ChainKit.Core.Crypto;
using ChainKit.Core.Extensions;
using ChainKit.Tron.Crypto;
using ChainKit.Tron.Models;
using ChainKit.Tron.Protocol;
using ChainKit.Tron.Providers;

namespace ChainKit.Tron.Contracts;

/// <summary>
/// High-level wrapper for TRC20 token contract interaction.
/// Provides both read-only queries (via triggerConstantContract) and
/// write operations (via triggerSmartContract + sign + broadcast).
/// Token decimals are fetched lazily and cached for amount conversion.
/// </summary>
public class Trc20Contract : IDisposable
{
    private const long DefaultFeeLimit = 100_000_000; // 100 TRX

    public string ContractAddress { get; }

    private readonly ITronProvider _provider;
    private readonly TronAccount _ownerAccount;
    private readonly string _contractHex;
    private readonly SemaphoreSlim _decimalsLock = new(1, 1);
    private byte? _cachedDecimals;

    public Trc20Contract(ITronProvider provider, string contractAddress, TronAccount ownerAccount)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _ownerAccount = ownerAccount ?? throw new ArgumentNullException(nameof(ownerAccount));
        ContractAddress = contractAddress ?? throw new ArgumentNullException(nameof(contractAddress));
        _contractHex = ResolveHexAddress(contractAddress);
    }

    // --- Read-only (triggerConstantContract, no signing) ---

    /// <summary>
    /// Returns basic token metadata (name, symbol, decimals, totalSupply) in a single call.
    /// All four contract queries run in parallel.
    /// </summary>
    public async Task<TronResult<Trc20TokenInfo>> GetTokenInfoAsync(CancellationToken ct = default)
    {
        try
        {
            var nameTask = CallConstantAsync("name()", Array.Empty<byte>(), ct);
            var symbolTask = CallConstantAsync("symbol()", Array.Empty<byte>(), ct);
            var decimalsTask = GetDecimalsInternalAsync(ct);
            var supplyTask = CallConstantAsync("totalSupply()", Array.Empty<byte>(), ct);
            var contractTask = _provider.GetContractAsync(_contractHex, ct);

            await Task.WhenAll(nameTask, symbolTask, decimalsTask, supplyTask, contractTask);

            var name = AbiEncoder.DecodeString(nameTask.Result);
            var symbol = AbiEncoder.DecodeString(symbolTask.Result);
            var decimals = decimalsTask.Result;
            var rawSupply = AbiEncoder.DecodeUint256(supplyTask.Result);
            var totalSupply = ToDecimalAmount(rawSupply, decimals);
            var originAddress = FormatAddress(contractTask.Result.OriginAddress);

            return TronResult<Trc20TokenInfo>.Ok(new Trc20TokenInfo(name, symbol, decimals, totalSupply, originAddress));
        }
        catch (Exception ex)
        {
            return TronResult<Trc20TokenInfo>.Fail(TronErrorCode.ContractExecutionFailed, ex.Message, ex.ToString());
        }
    }

    /// <summary>Returns the token name.</summary>
    public async Task<TronResult<string>> NameAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await CallConstantAsync("name()", Array.Empty<byte>(), ct);
            return TronResult<string>.Ok(AbiEncoder.DecodeString(result));
        }
        catch (Exception ex)
        {
            return TronResult<string>.Fail(TronErrorCode.ContractExecutionFailed, ex.Message, ex.ToString());
        }
    }

    /// <summary>Returns the token symbol.</summary>
    public async Task<TronResult<string>> SymbolAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await CallConstantAsync("symbol()", Array.Empty<byte>(), ct);
            return TronResult<string>.Ok(AbiEncoder.DecodeString(result));
        }
        catch (Exception ex)
        {
            return TronResult<string>.Fail(TronErrorCode.ContractExecutionFailed, ex.Message, ex.ToString());
        }
    }

    /// <summary>Returns the number of decimals the token uses.</summary>
    public async Task<TronResult<byte>> DecimalsAsync(CancellationToken ct = default)
    {
        try
        {
            var decimals = await GetDecimalsInternalAsync(ct);
            return TronResult<byte>.Ok(decimals);
        }
        catch (Exception ex)
        {
            return TronResult<byte>.Fail(TronErrorCode.ContractExecutionFailed, ex.Message, ex.ToString());
        }
    }

    /// <summary>Returns the total token supply, converted using token decimals.</summary>
    public async Task<TronResult<decimal>> TotalSupplyAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await CallConstantAsync("totalSupply()", Array.Empty<byte>(), ct);
            var raw = AbiEncoder.DecodeUint256(result);
            var decimals = await GetDecimalsInternalAsync(ct);
            return TronResult<decimal>.Ok(ToDecimalAmount(raw, decimals));
        }
        catch (Exception ex)
        {
            return TronResult<decimal>.Fail(TronErrorCode.ContractExecutionFailed, ex.Message, ex.ToString());
        }
    }

    /// <summary>Returns the token balance for the given address, converted using token decimals.</summary>
    public async Task<TronResult<decimal>> BalanceOfAsync(string address, CancellationToken ct = default)
    {
        try
        {
            var addrHex = ResolveHexAddress(address);
            var param = TronAbiEncoder.EncodeAddress(addrHex);
            var result = await CallConstantAsync("balanceOf(address)", param, ct);
            var raw = AbiEncoder.DecodeUint256(result);
            var decimals = await GetDecimalsInternalAsync(ct);
            return TronResult<decimal>.Ok(ToDecimalAmount(raw, decimals));
        }
        catch (Exception ex)
        {
            return TronResult<decimal>.Fail(TronErrorCode.ContractExecutionFailed, ex.Message, ex.ToString());
        }
    }

    /// <summary>Returns the amount the spender is allowed to spend on behalf of the owner.</summary>
    public async Task<TronResult<decimal>> AllowanceAsync(string owner, string spender, CancellationToken ct = default)
    {
        try
        {
            var ownerHex = ResolveHexAddress(owner);
            var spenderHex = ResolveHexAddress(spender);
            var param = ConcatBytes(TronAbiEncoder.EncodeAddress(ownerHex), TronAbiEncoder.EncodeAddress(spenderHex));
            var result = await CallConstantAsync("allowance(address,address)", param, ct);
            var raw = AbiEncoder.DecodeUint256(result);
            var decimals = await GetDecimalsInternalAsync(ct);
            return TronResult<decimal>.Ok(ToDecimalAmount(raw, decimals));
        }
        catch (Exception ex)
        {
            return TronResult<decimal>.Fail(TronErrorCode.ContractExecutionFailed, ex.Message, ex.ToString());
        }
    }

    // --- Write (triggerSmartContract -> sign -> broadcast) ---

    /// <summary>Transfers tokens to the given address.</summary>
    public async Task<TronResult<TransferResult>> TransferAsync(string to, decimal amount, CancellationToken ct = default)
    {
        if (amount <= 0)
            return TronResult<TransferResult>.Fail(TronErrorCode.InvalidAmount, "Amount must be positive");
        try
        {
            var toHex = ResolveHexAddress(to);
            var decimals = await GetDecimalsInternalAsync(ct);
            var rawAmount = ToRawAmount(amount, decimals);
            var data = TronAbiEncoder.EncodeTransfer(toHex, rawAmount);

            return await ExecuteWriteAsync("transfer(address,uint256)", data, to, amount, ct);
        }
        catch (Exception ex)
        {
            return TronResult<TransferResult>.Fail(TronErrorCode.ContractExecutionFailed, ex.Message, ex.ToString());
        }
    }

    /// <summary>Approves the spender to spend the given amount of tokens.</summary>
    public async Task<TronResult<TransferResult>> ApproveAsync(string spender, decimal amount, CancellationToken ct = default)
    {
        if (amount <= 0)
            return TronResult<TransferResult>.Fail(TronErrorCode.InvalidAmount, "Amount must be positive");
        try
        {
            var spenderHex = ResolveHexAddress(spender);
            var decimals = await GetDecimalsInternalAsync(ct);
            var rawAmount = ToRawAmount(amount, decimals);
            var data = TronAbiEncoder.EncodeApprove(spenderHex, rawAmount);

            return await ExecuteWriteAsync("approve(address,uint256)", data, spender, amount, ct);
        }
        catch (Exception ex)
        {
            return TronResult<TransferResult>.Fail(TronErrorCode.ContractExecutionFailed, ex.Message, ex.ToString());
        }
    }

    /// <summary>Mints new tokens to the given address (requires minter role).</summary>
    public async Task<TronResult<TransferResult>> MintAsync(string to, decimal amount, CancellationToken ct = default)
    {
        if (amount <= 0)
            return TronResult<TransferResult>.Fail(TronErrorCode.InvalidAmount, "Amount must be positive");
        try
        {
            var toHex = ResolveHexAddress(to);
            var decimals = await GetDecimalsInternalAsync(ct);
            var rawAmount = ToRawAmount(amount, decimals);
            var data = TronAbiEncoder.EncodeMint(toHex, rawAmount);

            return await ExecuteWriteAsync("mint(address,uint256)", data, to, amount, ct);
        }
        catch (Exception ex)
        {
            return TronResult<TransferResult>.Fail(TronErrorCode.ContractExecutionFailed, ex.Message, ex.ToString());
        }
    }

    /// <summary>Burns tokens from the caller's balance.</summary>
    public async Task<TronResult<TransferResult>> BurnAsync(decimal amount, CancellationToken ct = default)
    {
        if (amount <= 0)
            return TronResult<TransferResult>.Fail(TronErrorCode.InvalidAmount, "Amount must be positive");
        try
        {
            var decimals = await GetDecimalsInternalAsync(ct);
            var rawAmount = ToRawAmount(amount, decimals);
            var data = TronAbiEncoder.EncodeBurn(rawAmount);

            return await ExecuteWriteAsync("burn(uint256)", data, _ownerAccount.Address, amount, ct);
        }
        catch (Exception ex)
        {
            return TronResult<TransferResult>.Fail(TronErrorCode.ContractExecutionFailed, ex.Message, ex.ToString());
        }
    }

    /// <summary>Burns tokens from the specified address (requires allowance).</summary>
    public async Task<TronResult<TransferResult>> BurnFromAsync(string from, decimal amount, CancellationToken ct = default)
    {
        if (amount <= 0)
            return TronResult<TransferResult>.Fail(TronErrorCode.InvalidAmount, "Amount must be positive");
        try
        {
            var fromHex = ResolveHexAddress(from);
            var decimals = await GetDecimalsInternalAsync(ct);
            var rawAmount = ToRawAmount(amount, decimals);
            var data = TronAbiEncoder.EncodeBurnFrom(fromHex, rawAmount);

            return await ExecuteWriteAsync("burnFrom(address,uint256)", data, from, amount, ct);
        }
        catch (Exception ex)
        {
            return TronResult<TransferResult>.Fail(TronErrorCode.ContractExecutionFailed, ex.Message, ex.ToString());
        }
    }

    // === Internal helpers ===

    private async Task<byte[]> CallConstantAsync(string functionSelector, byte[] parameter, CancellationToken ct)
    {
        return await _provider.TriggerConstantContractAsync(
            _ownerAccount.HexAddress, _contractHex, functionSelector, parameter, ct);
    }

    private async Task<TronResult<TransferResult>> ExecuteWriteAsync(
        string functionSelector, byte[] fullData, string toAddress, decimal amount, CancellationToken ct)
    {
        // Strip the 4-byte selector from fullData since the provider adds it via functionSelector
        var parameter = fullData.Length > 4 ? fullData[4..] : Array.Empty<byte>();

        var tx = await _provider.TriggerSmartContractAsync(
            _ownerAccount.HexAddress, _contractHex,
            functionSelector, parameter,
            DefaultFeeLimit, 0, ct);

        var signed = TransactionUtils.Sign(tx, _ownerAccount.PrivateKey);
        var txId = TransactionUtils.ComputeTxId(signed).ToHex();

        var broadcastResult = await _provider.BroadcastTransactionAsync(signed, ct);

        if (!broadcastResult.Success)
        {
            return TronResult<TransferResult>.Fail(
                TronErrorCode.ContractExecutionFailed,
                broadcastResult.Message ?? "Broadcast failed",
                broadcastResult.Message);
        }

        return TronResult<TransferResult>.Ok(
            new TransferResult(broadcastResult.TxId ?? txId, _ownerAccount.Address, toAddress, amount));
    }

    private async Task<byte> GetDecimalsInternalAsync(CancellationToken ct)
    {
        if (_cachedDecimals.HasValue)
            return _cachedDecimals.Value;

        await _decimalsLock.WaitAsync(ct);
        try
        {
            if (_cachedDecimals.HasValue)
                return _cachedDecimals.Value;

            var result = await CallConstantAsync("decimals()", Array.Empty<byte>(), ct);
            var raw = AbiEncoder.DecodeUint256(result);
            _cachedDecimals = (byte)raw;
            return _cachedDecimals.Value;
        }
        finally
        {
            _decimalsLock.Release();
        }
    }

    private static decimal ToDecimalAmount(BigInteger rawValue, byte decimals)
    {
        var divisor = BigInteger.Pow(10, decimals);
        var wholePart = BigInteger.DivRem(rawValue, divisor, out var remainder);
        return (decimal)wholePart + (decimal)remainder / (decimal)divisor;
    }

    private static BigInteger ToRawAmount(decimal amount, byte decimals)
    {
        var multiplier = TronConverter.DecimalPow10(decimals);
        return new BigInteger(amount * multiplier);
    }

    private static string ResolveHexAddress(string address)
    {
        if (address.StartsWith("T"))
            return TronAddress.ToHex(address);
        return address;
    }

    private static string FormatAddress(string address)
    {
        if (string.IsNullOrEmpty(address)) return address;
        if (address.Length == 42 && address.StartsWith("41", StringComparison.OrdinalIgnoreCase)
            && address.All(c => char.IsAsciiHexDigit(c)))
        {
            try { return TronAddress.ToBase58(address); }
            catch { return address; }
        }
        return address;
    }

    private static byte[] ConcatBytes(byte[] a, byte[] b)
    {
        var result = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, result, 0, a.Length);
        Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
        return result;
    }

    public void Dispose()
    {
        _decimalsLock.Dispose();
    }
}
