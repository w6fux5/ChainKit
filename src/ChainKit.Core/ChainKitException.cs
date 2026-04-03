namespace ChainKit.Core;

public class ChainKitException : Exception
{
    public ChainKitException(string message) : base(message) { }
    public ChainKitException(string message, Exception innerException) : base(message, innerException) { }
}
