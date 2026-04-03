namespace ChainKit.Core;

public interface IAccount
{
    string Address { get; }
    byte[] PublicKey { get; }
}
