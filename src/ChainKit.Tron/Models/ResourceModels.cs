using System.Numerics;

namespace ChainKit.Tron.Models;

public enum ResourceType { Bandwidth, Energy }

public record ResourceInfo(
    long BandwidthTotal, long BandwidthUsed,
    long EnergyTotal, long EnergyUsed,
    decimal StakedForBandwidth, decimal StakedForEnergy,
    IReadOnlyList<DelegationInfo> DelegationsOut,
    IReadOnlyList<DelegationInfo> DelegationsIn);

public record DelegationInfo(string Address, decimal Amount, ResourceType Resource, bool Locked);

/// <summary>
/// Resource exchange rate for bidirectional TRX ↔ Energy/Bandwidth conversion.
/// </summary>
public record ResourceExchangeRate(
    ResourceType Resource,
    decimal ResourcePerTrx,
    decimal TrxPerResource,
    long NetworkTotalStaked,
    long NetworkTotalResourceLimit)
{
    /// <summary>Estimates how much resource you get for the given TRX amount.</summary>
    public decimal EstimateResource(decimal trxAmount) => trxAmount * ResourcePerTrx;

    /// <summary>Estimates how much TRX you need to stake for the given resource amount.</summary>
    public decimal EstimateTrx(long resourceAmount) => resourceAmount * TrxPerResource;
}
public record StakeResult(string TxId, decimal Amount, ResourceType Resource);
public record UnstakeResult(string TxId, decimal Amount, ResourceType Resource);
public record DelegateResult(string TxId, string ReceiverAddress, decimal Amount, ResourceType Resource);
public record UndelegateResult(string TxId, string ReceiverAddress, decimal Amount, ResourceType Resource);
public record DeployResult(string TxId, string ContractAddress);

public record Trc20TokenOptions(
    string Name, string Symbol, byte Decimals,
    BigInteger InitialSupply,
    bool Mintable = true, bool Burnable = true);
