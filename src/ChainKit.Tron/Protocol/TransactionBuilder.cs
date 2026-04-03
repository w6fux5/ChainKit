using ChainKit.Core.Extensions;
using ChainKit.Tron.Protocol.Protobuf;
using Google.Protobuf;

namespace ChainKit.Tron.Protocol;

/// <summary>
/// Fluent builder for constructing Tron <see cref="Transaction"/> protobuf messages.
/// </summary>
public class TransactionBuilder
{
    private Transaction.Types.raw _raw = new();

    /// <summary>
    /// Creates a TRX transfer transaction.
    /// </summary>
    /// <param name="ownerAddressHex">Sender address as hex (with 41 prefix).</param>
    /// <param name="toAddressHex">Recipient address as hex (with 41 prefix).</param>
    /// <param name="amount">Amount in sun (1 TRX = 1,000,000 sun).</param>
    public TransactionBuilder CreateTransfer(string ownerAddressHex, string toAddressHex, long amount)
    {
        var contract = new TransferContract
        {
            OwnerAddress = ByteString.CopyFrom(ownerAddressHex.FromHex()),
            ToAddress = ByteString.CopyFrom(toAddressHex.FromHex()),
            Amount = amount
        };

        var contractWrapper = new Transaction.Types.Contract
        {
            Type = Transaction.Types.Contract.Types.ContractType.TransferContract,
            Parameter = Google.Protobuf.WellKnownTypes.Any.Pack(contract, "type.googleapis.com")
        };

        _raw.Contract.Add(contractWrapper);
        _raw.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _raw.Expiration = _raw.Timestamp + 60 * 60 * 1000; // 1 hour default

        return this;
    }

    /// <summary>
    /// Creates a smart contract trigger transaction (e.g., TRC20 transfer).
    /// </summary>
    /// <param name="ownerAddressHex">Caller address as hex (with 41 prefix).</param>
    /// <param name="contractAddressHex">Contract address as hex (with 41 prefix).</param>
    /// <param name="data">ABI-encoded function call data.</param>
    /// <param name="callValue">TRX value to send with the call (in sun), default 0.</param>
    public TransactionBuilder TriggerContract(string ownerAddressHex, string contractAddressHex, byte[] data, long callValue = 0)
    {
        var trigger = new TriggerSmartContract
        {
            OwnerAddress = ByteString.CopyFrom(ownerAddressHex.FromHex()),
            ContractAddress = ByteString.CopyFrom(contractAddressHex.FromHex()),
            Data = ByteString.CopyFrom(data),
            CallValue = callValue
        };

        var contractWrapper = new Transaction.Types.Contract
        {
            Type = Transaction.Types.Contract.Types.ContractType.TriggerSmartContract,
            Parameter = Google.Protobuf.WellKnownTypes.Any.Pack(trigger, "type.googleapis.com")
        };

        _raw.Contract.Add(contractWrapper);
        _raw.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _raw.Expiration = _raw.Timestamp + 60 * 60 * 1000;

        return this;
    }

    /// <summary>
    /// Sets the fee limit for the transaction (required for smart contract calls).
    /// </summary>
    /// <param name="feeLimit">Fee limit in sun.</param>
    public TransactionBuilder SetFeeLimit(long feeLimit)
    {
        _raw.FeeLimit = feeLimit;
        return this;
    }

    /// <summary>
    /// Sets the reference block bytes and hash (used for transaction deduplication).
    /// </summary>
    /// <param name="refBlockBytes">Last 2 bytes of the reference block number.</param>
    /// <param name="refBlockHash">First 8 bytes of the reference block hash.</param>
    public TransactionBuilder SetRefBlock(byte[] refBlockBytes, byte[] refBlockHash)
    {
        _raw.RefBlockBytes = ByteString.CopyFrom(refBlockBytes);
        _raw.RefBlockHash = ByteString.CopyFrom(refBlockHash);
        return this;
    }

    /// <summary>
    /// Sets the transaction expiration time.
    /// </summary>
    /// <param name="expirationMs">Expiration as Unix timestamp in milliseconds.</param>
    public TransactionBuilder SetExpiration(long expirationMs)
    {
        _raw.Expiration = expirationMs;
        return this;
    }

    /// <summary>
    /// Sets a memo/data field on the transaction.
    /// </summary>
    /// <param name="memo">Memo bytes.</param>
    public TransactionBuilder SetMemo(byte[] memo)
    {
        _raw.Data = ByteString.CopyFrom(memo);
        return this;
    }

    /// <summary>
    /// Sets the transaction timestamp.
    /// </summary>
    /// <param name="timestampMs">Timestamp as Unix timestamp in milliseconds.</param>
    public TransactionBuilder SetTimestamp(long timestampMs)
    {
        _raw.Timestamp = timestampMs;
        return this;
    }

    /// <summary>
    /// Builds and returns the <see cref="Transaction"/> protobuf message.
    /// </summary>
    public Transaction Build()
    {
        if (_raw.Contract.Count == 0)
            throw new InvalidOperationException("Transaction must have at least one contract.");

        var tx = new Transaction
        {
            RawData = _raw
        };

        return tx;
    }
}
