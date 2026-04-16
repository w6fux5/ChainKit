namespace ChainKit.Core;

/// <summary>
/// Thrown for SDK-internal bugs (invalid state, unexpected conditions). Business errors
/// (insufficient balance, invalid address, contract revert, etc.) are returned via the
/// Result pattern instead of thrown — catching this type indicates a bug, not a validation failure.
/// </summary>
public class ChainKitException : Exception
{
    /// <summary>Initializes a new instance with the given message.</summary>
    public ChainKitException(string message) : base(message) { }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    public ChainKitException(string message, Exception innerException) : base(message, innerException) { }
}
