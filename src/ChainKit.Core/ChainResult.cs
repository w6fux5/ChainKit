namespace ChainKit.Core;

/// <summary>
/// Generic Result wrapper for chain operations — success carries Data, failure carries Error.
/// Use via chain-specific subtypes (TronResult&lt;T&gt;, EvmResult&lt;T&gt;) which add convenience factories.
/// </summary>
/// <typeparam name="T">Type of the successful result payload.</typeparam>
public record ChainResult<T>
{
    /// <summary>True when the operation succeeded and Data is populated.</summary>
    public bool Success { get; protected init; }

    /// <summary>The successful result payload; default when Success is false.</summary>
    public T? Data { get; protected init; }

    /// <summary>Error details when Success is false; null on success.</summary>
    public ChainError? Error { get; protected init; }

    /// <summary>Protected default constructor — construct via Ok/Fail factories.</summary>
    protected ChainResult() { }

    /// <summary>Creates a successful result with the given data.</summary>
    public static ChainResult<T> Ok(T data) => new()
    {
        Success = true,
        Data = data,
        Error = null
    };

    /// <summary>Creates a failed result with the given error.</summary>
    public static ChainResult<T> Fail(ChainError error) => new()
    {
        Success = false,
        Data = default,
        Error = error
    };
}
