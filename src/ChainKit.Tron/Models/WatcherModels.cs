namespace ChainKit.Tron.Models;

public record TronBlock(
    long BlockNumber, string BlockId,
    DateTimeOffset Timestamp,
    IReadOnlyList<TronBlockTransaction> Transactions);

public record TronBlockTransaction(
    string TxId, string FromAddress, string ToAddress,
    string ContractType, byte[] RawData);

// --- Discovery events (fire once at Unconfirmed) ---

public record TrxReceivedEventArgs(
    string TxId, string FromAddress, string ToAddress,
    decimal Amount, long BlockNumber, DateTimeOffset Timestamp);

public record TrxSentEventArgs(
    string TxId, string FromAddress, string ToAddress,
    decimal Amount, long BlockNumber, DateTimeOffset Timestamp);

public record Trc20ReceivedEventArgs(
    string TxId, string FromAddress, string ToAddress,
    string ContractAddress, string Symbol,
    decimal RawAmount,
    decimal? Amount,
    int Decimals,
    long BlockNumber, DateTimeOffset Timestamp);

public record Trc20SentEventArgs(
    string TxId, string FromAddress, string ToAddress,
    string ContractAddress, string Symbol,
    decimal RawAmount,
    decimal? Amount,
    int Decimals,
    long BlockNumber, DateTimeOffset Timestamp);

// --- Final-state events ---

public record TransactionConfirmedEventArgs(
    string TxId, long BlockNumber, DateTimeOffset Timestamp);

public record TransactionFailedEventArgs(
    string TxId, long BlockNumber,
    TransactionFailureReason Reason, string? Message);

/// <summary>
/// Failure reasons for watcher transaction lifecycle events.
/// </summary>
public enum TransactionFailureReason
{
    /// <summary>Contract execution reverted (e.g. insufficient token balance).</summary>
    ContractReverted,
    /// <summary>Not enough Energy to complete contract execution.</summary>
    OutOfEnergy,
    /// <summary>Contract execution exceeded time limit.</summary>
    OutOfTime,
    /// <summary>Token transfer failed at contract level.</summary>
    TransferFailed,
    /// <summary>Solidity Node did not confirm within the maximum pending age.</summary>
    Expired,
    /// <summary>Other or unknown failure (EVM internal errors, etc.).</summary>
    Other
}
