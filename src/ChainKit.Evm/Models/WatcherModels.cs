using System.Numerics;
using System.Text.Json;

namespace ChainKit.Evm.Models;

public record EvmBlock(long BlockNumber, string BlockHash, DateTimeOffset Timestamp, List<EvmBlockTransaction> Transactions);
public record EvmBlockTransaction(string TxHash, string From, string To, BigInteger Value, byte[] Input, JsonElement? Receipt);

public record NativeReceivedEventArgs(string TxId, string FromAddress, string ToAddress, decimal? Amount, BigInteger RawAmount);
public record NativeSentEventArgs(string TxId, string FromAddress, string ToAddress, decimal? Amount, BigInteger RawAmount);
public record Erc20ReceivedEventArgs(string TxId, string ContractAddress, string FromAddress, string ToAddress, BigInteger RawAmount, decimal? Amount, string? Symbol);
public record Erc20SentEventArgs(string TxId, string ContractAddress, string FromAddress, string ToAddress, BigInteger RawAmount, decimal? Amount, string? Symbol);
public record TransactionConfirmedEventArgs(string TxId, long BlockNumber);
public record TransactionFailedEventArgs(string TxId, string Reason);
