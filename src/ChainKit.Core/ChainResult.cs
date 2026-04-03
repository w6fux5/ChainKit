namespace ChainKit.Core;

public record ChainResult<T>
{
    public bool Success { get; private init; }
    public T? Data { get; private init; }
    public ChainError? Error { get; private init; }

    private ChainResult() { }

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
