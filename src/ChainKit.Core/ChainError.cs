namespace ChainKit.Core;

public record ChainError(
    string Code,
    string Message,
    string? RawMessage);
