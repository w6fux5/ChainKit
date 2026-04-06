namespace ChainKit.Tron.Models;

public record Trc20BalanceInfo(decimal RawBalance, decimal? Balance, string Symbol, int Decimals);

public record BalanceInfo(decimal TrxBalance, IReadOnlyDictionary<string, Trc20BalanceInfo> Trc20Balances);

// Low-level DTOs (used by ITronProvider)

/// <summary>
/// Account information returned by the /wallet/getaccount endpoint.
/// FrozenBalanceForBandwidth and FrozenBalanceForEnergy contain the total staked
/// amounts (in SUN) from the Stake 2.0 frozenV2 array.
/// </summary>
public record AccountInfo(
    string Address, long Balance, long NetUsage, long EnergyUsage, long CreateTime,
    long FrozenBalanceForBandwidth = 0,
    long FrozenBalanceForEnergy = 0);
public record BlockInfo(
    long BlockNumber, string BlockId, long Timestamp, int TransactionCount, byte[] BlockHeaderRawData,
    IReadOnlyList<BlockTransactionInfo>? Transactions = null);

public record BlockTransactionInfo(
    string TxId, string ContractType,
    string OwnerAddress, string ToAddress,
    long Amount, string? ContractAddress, byte[]? Data);
public record BroadcastResult(bool Success, string? TxId, string? Message);
public record TransactionInfoDto(
    string TxId, long BlockNumber, long BlockTimestamp, string ContractResult, long Fee, long EnergyUsage, long NetUsage,
    // Contract detail fields (populated by GetTransactionByIdAsync)
    string ContractType = "", string OwnerAddress = "", string ToAddress = "",
    long AmountSun = 0, string? ContractAddress = null, string? ContractData = null,
    // Resource TRX costs in Sun (populated by GetTransactionInfoByIdAsync from receipt)
    long EnergyFee = 0, long NetFee = 0,
    // Contract execution result from receipt (e.g. "SUCCESS", "REVERT", "OUT_OF_ENERGY")
    string ReceiptResult = "");
public record AccountResourceInfo(long FreeBandwidthLimit, long FreeBandwidthUsed, long EnergyLimit, long EnergyUsed, long TotalBandwidthLimit, long TotalBandwidthUsed);

// Delegation DTOs (used by ITronProvider)

public record DelegatedResourceIndex(
    IReadOnlyList<string> ToAddresses,
    IReadOnlyList<string> FromAddresses);

public record DelegatedResourceInfo(
    string From, string To,
    long FrozenBalanceForBandwidth,
    long FrozenBalanceForEnergy);
