namespace ChainKit.Tron.Models;

public enum TronErrorCode
{
    Unknown,
    InvalidAddress,
    InvalidAmount,
    InsufficientBalance,
    InsufficientEnergy,
    InsufficientBandwidth,
    ContractExecutionFailed,
    ContractValidationFailed,
    TransactionExpired,
    DuplicateTransaction,
    ProviderConnectionFailed,
    ProviderTimeout
}
