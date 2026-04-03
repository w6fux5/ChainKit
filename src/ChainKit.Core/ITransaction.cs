namespace ChainKit.Core;

public interface ITransaction
{
    string TxId { get; }
    string FromAddress { get; }
    string ToAddress { get; }
    decimal Amount { get; }
    DateTimeOffset Timestamp { get; }
}
