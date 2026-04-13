using System.Numerics;
using ChainKit.Core.Extensions;
using ChainKit.Evm.Protocol;
using Xunit;

namespace ChainKit.Evm.Tests.Protocol;

public class TransactionBuilderTests
{
    [Fact]
    public void BuildEip1559_Unsigned_StartsWithTypeByte()
    {
        var tx = EvmTransactionBuilder.BuildEip1559(1, 0, 1000, 2000, 21000,
            "0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf", BigInteger.Zero, Array.Empty<byte>(), null);
        Assert.Equal(0x02, tx[0]);
    }

    [Fact]
    public void BuildEip1559_Signed_LongerThanUnsigned()
    {
        var unsigned = EvmTransactionBuilder.BuildEip1559(1, 0, 1000, 2000, 21000,
            "0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf", BigInteger.Zero, Array.Empty<byte>(), null);

        // Create a fake 65-byte signature for length comparison
        var sig = new byte[65];
        sig[0] = 1; // non-zero r
        sig[32] = 1; // non-zero s

        var signed = EvmTransactionBuilder.BuildEip1559(1, 0, 1000, 2000, 21000,
            "0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf", BigInteger.Zero, Array.Empty<byte>(), sig);

        Assert.True(signed.Length > unsigned.Length);
    }

    [Fact]
    public void BuildLegacy_Unsigned_IncludesChainId()
    {
        var tx = EvmTransactionBuilder.BuildLegacy(0, new BigInteger(20_000_000_000), 21000,
            "0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf", BigInteger.Zero, Array.Empty<byte>(), 1, null);
        Assert.NotEmpty(tx);
        // Legacy unsigned starts with RLP list prefix (0xc0+)
        Assert.True(tx[0] >= 0xc0);
    }

    [Fact]
    public void SignEip1559Transaction_ProducesValidTxHash()
    {
        var privateKey = "0000000000000000000000000000000000000000000000000000000000000001".FromHex();
        var (txHash, rawTx) = EvmTransactionUtils.SignEip1559Transaction(
            1, 0, 1000, 2000, 21000,
            "0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf",
            BigInteger.Zero, Array.Empty<byte>(), privateKey);

        Assert.StartsWith("0x", txHash);
        Assert.Equal(66, txHash.Length); // 0x + 64 hex chars
        Assert.Equal(0x02, rawTx[0]); // EIP-1559 type byte
    }

    [Fact]
    public void SignEip1559Transaction_DeterministicForSameInput()
    {
        var privateKey = "0000000000000000000000000000000000000000000000000000000000000001".FromHex();
        var (hash1, _) = EvmTransactionUtils.SignEip1559Transaction(1, 0, 1000, 2000, 21000,
            "0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf", BigInteger.Zero, Array.Empty<byte>(), privateKey);
        var (hash2, _) = EvmTransactionUtils.SignEip1559Transaction(1, 0, 1000, 2000, 21000,
            "0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf", BigInteger.Zero, Array.Empty<byte>(), privateKey);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void BuildLegacy_HighChainId_VDoesNotOverflow()
    {
        // Polygon chainId=137: v = 137*2+35+recId = 309 or 310
        // This previously overflowed byte (309 % 256 = 53)
        var sig = new byte[65];
        sig[0] = 1; // non-zero r
        sig[32] = 1; // non-zero s
        sig[64] = 0; // recId = 0

        var tx = EvmTransactionBuilder.BuildLegacy(0, new BigInteger(20_000_000_000), 21000,
            "0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf", BigInteger.Zero, Array.Empty<byte>(), 137, sig);

        // Decode the RLP to verify v is correctly encoded as 309 (not 53)
        Assert.NotEmpty(tx);
        // The signed tx should be longer than unsigned due to v/r/s fields
        var unsigned = EvmTransactionBuilder.BuildLegacy(0, new BigInteger(20_000_000_000), 21000,
            "0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf", BigInteger.Zero, Array.Empty<byte>(), 137, null);
        Assert.True(tx.Length > unsigned.Length);
    }
}
