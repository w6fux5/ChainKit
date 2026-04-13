using System.Numerics;
using ChainKit.Core;

namespace ChainKit.Evm.Models;

public record TransferResult(string TxId);

public enum TransactionStatus { Unconfirmed, Confirmed, Failed }

public enum TransactionType { NativeTransfer, ContractCall, ContractCreation, Erc20Transfer }

public record EvmTransactionDetail : ITransaction
{
    public required string TxId { get; init; }
    public required string FromAddress { get; init; }
    public required string ToAddress { get; init; }
    public decimal Amount { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public TransactionStatus Status { get; init; }
    public long BlockNumber { get; init; }
    public long Nonce { get; init; }
    public long GasUsed { get; init; }
    public BigInteger GasPrice { get; init; }
    public decimal Fee { get; init; }
    public TokenTransferInfo? TokenTransfer { get; init; }
    public FailureInfo? Failure { get; init; }
}

public record TokenTransferInfo(
    string ContractAddress, string FromAddress, string ToAddress,
    BigInteger RawAmount, decimal? Amount, string? Symbol);

public record FailureInfo(string Reason, string? RevertData);
