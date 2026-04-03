namespace ChainKit.Tron.Models;

public record BalanceInfo(decimal TrxBalance, IReadOnlyDictionary<string, decimal> Trc20Balances);

public record AccountOverview(
    string Address, decimal TrxBalance,
    long Bandwidth, long BandwidthUsed,
    long Energy, long EnergyUsed,
    IReadOnlyList<TronTransactionDetail> RecentTransactions);

// Low-level DTOs (used by ITronProvider)
public record AccountInfo(string Address, long Balance, long NetUsage, long EnergyUsage, long CreateTime);
public record BlockInfo(long BlockNumber, string BlockId, long Timestamp, int TransactionCount, byte[] BlockHeaderRawData);
public record BroadcastResult(bool Success, string? TxId, string? Message);
public record TransactionInfoDto(
    string TxId, long BlockNumber, long BlockTimestamp, string ContractResult, long Fee, long EnergyUsage, long NetUsage,
    // Contract detail fields (populated by GetTransactionByIdAsync)
    string ContractType = "", string OwnerAddress = "", string ToAddress = "",
    long AmountSun = 0, string? ContractAddress = null, string? ContractData = null);
public record AccountResourceInfo(long FreeBandwidthLimit, long FreeBandwidthUsed, long EnergyLimit, long EnergyUsed, long TotalBandwidthLimit, long TotalBandwidthUsed);
