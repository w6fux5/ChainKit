namespace ChainKit.Tron.Models;

public record TransferResult(string TxId, string FromAddress, string ToAddress, decimal Amount);

public record TronTransactionDetail(
    string TxId, string FromAddress, string ToAddress,
    TransactionStatus Status, FailureInfo? Failure,
    TransactionType Type,
    decimal Amount, TokenTransferInfo? TokenTransfer,
    long? BlockNumber, DateTimeOffset? Timestamp,
    ResourceCost? Cost);

public enum TransactionStatus { Unconfirmed = 0, Confirmed = 1, Failed = 2 }

public enum TransactionType
{
    NativeTransfer, Trc20Transfer, Trc10Transfer,
    ContractCall, ContractDeploy,
    Stake, Unstake, Delegate, Undelegate, Other
}

public record TokenTransferInfo(
    string ContractAddress,
    string Symbol,           // "" if unknown
    int Decimals,            // 0 if unknown
    decimal RawAmount,       // always correct, original on-chain value (smallest unit)
    decimal? Amount);        // converted human-readable amount, null if decimals unknown
public record ResourceCost(decimal TrxBurned, long BandwidthUsed, long EnergyUsed, decimal BandwidthTrxCost, decimal EnergyTrxCost);
public record FailureInfo(FailureReason Reason, string Message, string? RevertMessage, string? RawResult);

public enum FailureReason
{
    OutOfEnergy, OutOfBandwidth, InsufficientBalance,
    ContractReverted, ContractOutOfTime,
    InvalidSignature, Expired, DuplicateTransaction, Other
}
