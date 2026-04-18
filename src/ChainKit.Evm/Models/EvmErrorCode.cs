namespace ChainKit.Evm.Models;

public enum EvmErrorCode
{
    Unknown,
    InvalidAddress,
    InvalidAmount,
    InsufficientBalance,
    InsufficientGasBalance,
    NonceTooLow,
    NonceTooHigh,
    GasPriceTooLow,
    GasLimitExceeded,
    ContractReverted,
    ContractNotFound,
    TransactionNotFound,
    ProviderConnectionFailed,
    ProviderTimeout,
    ProviderRpcError,
    InvalidArgument
}
