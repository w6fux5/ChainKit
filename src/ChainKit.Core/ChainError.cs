namespace ChainKit.Core;

/// <summary>
/// Represents a business or protocol error returned by the Result pattern.
/// Thrown exceptions indicate SDK bugs; expected failures are surfaced as ChainError instead.
/// </summary>
/// <param name="Code">Stable, machine-readable error code (e.g. "InvalidAmount", "ContractExecutionFailed").</param>
/// <param name="Message">Human-readable error message.</param>
/// <param name="RawMessage">Optional raw response from the node or underlying exception, useful for debugging.</param>
public record ChainError(
    string Code,
    string Message,
    string? RawMessage);
