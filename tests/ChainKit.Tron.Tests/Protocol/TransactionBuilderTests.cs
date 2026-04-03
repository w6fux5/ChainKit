using ChainKit.Core.Extensions;
using ChainKit.Tron.Protocol;
using Xunit;

namespace ChainKit.Tron.Tests.Protocol;

public class TransactionBuilderTests
{
    private const string FromHex = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
    private const string ToHex = "41b2a3f846af3b8a1b1f2c3d4e5f6a7b8c9d0e1f2a";

    [Fact]
    public void CreateTransfer_BuildsValidTransaction()
    {
        var tx = new TransactionBuilder()
            .CreateTransfer(FromHex, ToHex, 1_000_000)
            .SetFeeLimit(1_000_000)
            .Build();

        Assert.NotNull(tx);
        Assert.NotNull(tx.RawData);
    }

    [Fact]
    public void ComputeTxId_Returns32Bytes()
    {
        var tx = new TransactionBuilder()
            .CreateTransfer(FromHex, ToHex, 1_000_000)
            .Build();

        var txId = TransactionUtils.ComputeTxId(tx);
        Assert.Equal(32, txId.Length);
    }

    [Fact]
    public void ComputeTxId_SameTransaction_SameId()
    {
        var tx = new TransactionBuilder()
            .CreateTransfer(FromHex, ToHex, 1_000_000)
            .Build();

        Assert.Equal(TransactionUtils.ComputeTxId(tx), TransactionUtils.ComputeTxId(tx));
    }

    [Fact]
    public void Sign_AddsSignatureToTransaction()
    {
        var privateKey = "0000000000000000000000000000000000000000000000000000000000000001".FromHex();
        var tx = new TransactionBuilder()
            .CreateTransfer(FromHex, ToHex, 1_000_000)
            .Build();

        var signed = TransactionUtils.Sign(tx, privateKey);
        Assert.NotEmpty(signed.Signature);
    }
}
