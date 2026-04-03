using ChainKit.Core;

namespace ChainKit.Tron.Models;

public record TronResult<T> : ChainResult<T>
{
    private TronResult() { }

    public new static TronResult<T> Ok(T data) => new()
    {
        Success = true,
        Data = data,
        Error = null
    };

    public static TronResult<T> Fail(TronErrorCode code, string message, string? nodeMessage = null) => new()
    {
        Success = false,
        Data = default,
        Error = new ChainError(code.ToString(), message, nodeMessage)
    };

    public new static TronResult<T> Fail(ChainError error) => new()
    {
        Success = false,
        Data = default,
        Error = error
    };
}
