using System.Numerics;

namespace ChainKit.Evm.Models;

public record BalanceInfo(decimal Balance, BigInteger RawBalance);

public record TokenBalanceInfo(
    string ContractAddress, BigInteger RawBalance,
    decimal? Balance, string? Symbol, int? Decimals);

public record TokenInfo(
    string ContractAddress, string Name, string Symbol,
    int Decimals, BigInteger TotalSupply, string? OriginAddress);

public record BlockInfo(long BlockNumber, string BlockHash, DateTimeOffset Timestamp, int TransactionCount);
