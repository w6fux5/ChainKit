using System.Numerics;
using ChainKit.Core.Extensions;
using ChainKit.Tron.Contracts;
using ChainKit.Tron.Crypto;
using ChainKit.Tron.Models;
using ChainKit.Tron.Providers;

namespace ChainKit.Tron.Watching;

public class TronTransactionWatcher : IAsyncDisposable
{
    private readonly ITronBlockStream _stream;
    private readonly ITronProvider? _provider;
    private readonly TokenInfoCache _tokenCache = new();
    private readonly HashSet<string> _watchedAddresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _watchTask;

    // TRC20 transfer(address,uint256) selector: a9059cbb
    private static readonly byte[] Trc20TransferSelector = { 0xa9, 0x05, 0x9c, 0xbb };

    public TronTransactionWatcher(ITronBlockStream stream, ITronProvider? provider = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _provider = provider;
    }

    public void WatchAddress(string address)
    {
        var normalized = NormalizeAddress(address);
        lock (_lock) { _watchedAddresses.Add(normalized); }
    }

    public void WatchAddresses(IEnumerable<string> addresses)
    {
        lock (_lock)
        {
            foreach (var addr in addresses)
                _watchedAddresses.Add(NormalizeAddress(addr));
        }
    }

    public void UnwatchAddress(string address)
    {
        var normalized = NormalizeAddress(address);
        lock (_lock) { _watchedAddresses.Remove(normalized); }
    }

    public event EventHandler<TrxReceivedEventArgs>? OnTrxReceived;
    public event EventHandler<Trc20ReceivedEventArgs>? OnTrc20Received;
    public event EventHandler<TransactionConfirmedEventArgs>? OnTransactionConfirmed;

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _watchTask = WatchLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_watchTask != null)
        {
            try { await _watchTask; }
            catch (OperationCanceledException) { }
        }
    }

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        await foreach (var block in _stream.StreamBlocksAsync(ct))
        {
            foreach (var tx in block.Transactions)
            {
                bool isWatched;
                lock (_lock)
                {
                    isWatched = _watchedAddresses.Contains(tx.ToAddress)
                             || _watchedAddresses.Contains(tx.FromAddress);
                }
                if (isWatched)
                    await ProcessTransactionAsync(tx, block, ct);
            }
        }
    }

    private async Task ProcessTransactionAsync(TronBlockTransaction tx, TronBlock block, CancellationToken ct)
    {
        // Determine transaction type and fire appropriate event
        if (tx.ContractType == "TransferContract")
        {
            bool toWatched;
            lock (_lock) { toWatched = _watchedAddresses.Contains(tx.ToAddress); }
            if (toWatched)
            {
                var amount = ParseTrxAmount(tx.RawData);
                OnTrxReceived?.Invoke(this, new TrxReceivedEventArgs(
                    tx.TxId, tx.FromAddress, tx.ToAddress,
                    amount, block.BlockNumber, block.Timestamp));
            }
        }
        else if (tx.ContractType == "TriggerSmartContract")
        {
            var trc20Info = ParseTrc20Transfer(tx.RawData);
            // Determine the actual recipient: if this is a TRC20 transfer,
            // the "to" in the ABI data is the real token recipient.
            var effectiveTo = trc20Info.Recipient ?? tx.ToAddress;

            bool toWatched;
            lock (_lock)
            {
                toWatched = _watchedAddresses.Contains(effectiveTo)
                         || _watchedAddresses.Contains(tx.ToAddress);
            }
            if (toWatched)
            {
                // Resolve token symbol + decimals if provider is available
                string symbol = "";
                decimal resolvedAmount = trc20Info.Amount;

                if (_provider is not null && !string.IsNullOrEmpty(trc20Info.ContractAddress))
                {
                    try
                    {
                        var tokenInfo = await _tokenCache.GetOrResolveAsync(
                            trc20Info.ContractAddress, _provider, ct);
                        symbol = tokenInfo.Symbol;
                        if (tokenInfo.Decimals > 0)
                            resolvedAmount = trc20Info.Amount / (decimal)Math.Pow(10, tokenInfo.Decimals);
                    }
                    catch { /* resolution failed — fire with raw values */ }
                }

                OnTrc20Received?.Invoke(this, new Trc20ReceivedEventArgs(
                    tx.TxId, tx.FromAddress, effectiveTo,
                    trc20Info.ContractAddress, symbol, resolvedAmount,
                    block.BlockNumber, block.Timestamp));
            }
        }

        // Always fire confirmed event for matched transactions
        OnTransactionConfirmed?.Invoke(this, new TransactionConfirmedEventArgs(
            tx.TxId, block.BlockNumber, true));
    }

    /// <summary>
    /// Parses the TRX amount from the RawData byte array.
    /// Supports two formats:
    /// 1. PollingBlockStream format: [8 bytes big-endian amount][...]
    /// 2. Protobuf format: TransferContract { field 3 = amount }
    /// Returns amount in TRX (not sun).
    /// </summary>
    internal static decimal ParseTrxAmount(byte[] rawData)
    {
        if (rawData == null || rawData.Length < 8)
            return 0m;

        // PollingBlockStream format: first 8 bytes are big-endian int64 amount in sun
        long amountSun = ReadBigEndianInt64(rawData, 0);

        if (amountSun > 0)
            return amountSun / 1_000_000m;

        // Fall back to protobuf parsing for ZMQ stream format
        try
        {
            var raw = Protocol.Protobuf.Transaction.Types.raw.Parser.ParseFrom(rawData);
            var contract = raw.Contract?.FirstOrDefault();
            if (contract?.Type == Protocol.Protobuf.Transaction.Types.Contract.Types.ContractType.TransferContract
                && contract.Parameter != null)
            {
                var transfer = contract.Parameter.Unpack<Protocol.Protobuf.TransferContract>();
                return transfer.Amount / 1_000_000m;
            }
        }
        catch { /* not valid protobuf, amount stays 0 */ }

        return 0m;
    }

    /// <summary>
    /// Parses TRC20 transfer info from the RawData byte array.
    /// </summary>
    internal static Trc20TransferInfo ParseTrc20Transfer(byte[] rawData)
    {
        if (rawData == null || rawData.Length < 16)
            return Trc20TransferInfo.Empty;

        // Try PollingBlockStream format first:
        // [8: amount][4: contractAddr len][contractAddr bytes][4: data len][data bytes]
        string contractAddress = "";
        byte[]? callData = null;
        string? recipient = null;
        decimal amount = 0m;

        try
        {
            long callValue = ReadBigEndianInt64(rawData, 0);
            int contractAddrLen = ReadBigEndianInt32(rawData, 8);

            if (contractAddrLen >= 0 && contractAddrLen < rawData.Length - 12)
            {
                if (contractAddrLen > 0)
                    contractAddress = System.Text.Encoding.UTF8.GetString(rawData, 12, contractAddrLen);

                int dataLenOffset = 12 + contractAddrLen;
                if (dataLenOffset + 4 <= rawData.Length)
                {
                    int dataLen = ReadBigEndianInt32(rawData, dataLenOffset);
                    if (dataLen >= 0 && dataLenOffset + 4 + dataLen <= rawData.Length)
                    {
                        callData = new byte[dataLen];
                        Buffer.BlockCopy(rawData, dataLenOffset + 4, callData, 0, dataLen);
                    }
                }
            }
        }
        catch
        {
            // Not polling format, try protobuf below
        }

        // If no call data from polling format, try protobuf format (ZMQ stream)
        if (callData == null || callData.Length == 0)
        {
            try
            {
                var raw = Protocol.Protobuf.Transaction.Types.raw.Parser.ParseFrom(rawData);
                var contract = raw.Contract?.FirstOrDefault();
                if (contract?.Type == Protocol.Protobuf.Transaction.Types.Contract.Types.ContractType.TriggerSmartContract
                    && contract.Parameter != null)
                {
                    var trigger = contract.Parameter.Unpack<Protocol.Protobuf.TriggerSmartContract>();
                    contractAddress = trigger.ContractAddress.ToByteArray().ToHex();
                    callData = trigger.Data.ToByteArray();
                }
            }
            catch { /* not valid protobuf */ }
        }

        // Decode TRC20 transfer(address,uint256) from call data
        if (callData != null && callData.Length >= 68
            && callData[0] == Trc20TransferSelector[0]
            && callData[1] == Trc20TransferSelector[1]
            && callData[2] == Trc20TransferSelector[2]
            && callData[3] == Trc20TransferSelector[3])
        {
            // Bytes [4..36] = address (padded to 32 bytes)
            // Bytes [36..68] = uint256 amount
            recipient = AbiEncoder.DecodeAddress(callData[4..36]);
            var rawAmount = AbiEncoder.DecodeUint256(callData[36..68]);
            // TRC20 amounts are in the token's smallest unit;
            // we return raw value here — caller can divide by 10^decimals
            amount = (decimal)rawAmount;
        }

        return new Trc20TransferInfo(contractAddress, recipient, amount);
    }

    private static long ReadBigEndianInt64(byte[] buf, int offset)
    {
        return ((long)buf[offset] << 56)
             | ((long)buf[offset + 1] << 48)
             | ((long)buf[offset + 2] << 40)
             | ((long)buf[offset + 3] << 32)
             | ((long)buf[offset + 4] << 24)
             | ((long)buf[offset + 5] << 16)
             | ((long)buf[offset + 6] << 8)
             | (long)buf[offset + 7];
    }

    private static int ReadBigEndianInt32(byte[] buf, int offset)
    {
        return (buf[offset] << 24)
             | (buf[offset + 1] << 16)
             | (buf[offset + 2] << 8)
             | buf[offset + 3];
    }

    private static string NormalizeAddress(string address)
    {
        // Convert Base58 to hex for consistent comparison
        if (address.StartsWith('T'))
            return Crypto.TronAddress.ToHex(address);
        return address.ToLowerInvariant();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }

    /// <summary>
    /// Holds parsed TRC20 transfer details.
    /// </summary>
    internal record Trc20TransferInfo(string ContractAddress, string? Recipient, decimal Amount)
    {
        public static readonly Trc20TransferInfo Empty = new("", null, 0m);
    }
}
