namespace ChainKit.Tron.Models;

public record TronBlock(
    long BlockNumber, string BlockId,
    DateTimeOffset Timestamp,
    IReadOnlyList<TronBlockTransaction> Transactions);

public record TronBlockTransaction(
    string TxId, string FromAddress, string ToAddress,
    string ContractType, byte[] RawData);

public record TrxReceivedEventArgs(
    string TxId, string FromAddress, string ToAddress,
    decimal Amount, long BlockNumber, DateTimeOffset Timestamp);

public record Trc20ReceivedEventArgs(
    string TxId, string FromAddress, string ToAddress,
    string ContractAddress, string Symbol,
    decimal RawAmount,       // always correct
    decimal? Amount,         // null if decimals unknown
    int Decimals,            // 0 if unknown
    long BlockNumber, DateTimeOffset Timestamp);

public record TransactionConfirmedEventArgs(
    string TxId, long BlockNumber, bool Success);
