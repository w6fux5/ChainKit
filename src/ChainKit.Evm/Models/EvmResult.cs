using ChainKit.Core;

namespace ChainKit.Evm.Models;

public record EvmResult<T> : ChainResult<T>
{
    private EvmResult() { }

    public EvmErrorCode? ErrorCode { get; init; }

    public new static EvmResult<T> Ok(T data) => new()
    {
        Success = true,
        Data = data,
        Error = null
    };

    public static EvmResult<T> Fail(EvmErrorCode code, string message, string? rawMessage = null) => new()
    {
        Success = false,
        Data = default,
        ErrorCode = code,
        Error = new ChainError(code.ToString(), message, rawMessage)
    };

    public new static EvmResult<T> Fail(ChainError error) => new()
    {
        Success = false,
        Data = default,
        Error = error
    };
}
