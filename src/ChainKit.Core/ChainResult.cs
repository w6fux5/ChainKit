namespace ChainKit.Core;

public record ChainResult<T>
{
    public bool Success { get; protected init; }
    public T? Data { get; protected init; }
    public ChainError? Error { get; protected init; }

    protected ChainResult() { }

    public static ChainResult<T> Ok(T data) => new()
    {
        Success = true,
        Data = data,
        Error = null
    };

    public static ChainResult<T> Fail(ChainError error) => new()
    {
        Success = false,
        Data = default,
        Error = error
    };
}
