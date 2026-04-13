using System.Numerics;
using ChainKit.Core.Converters;
using Xunit;

namespace ChainKit.Core.Tests.Converters;

public class TokenConverterTests
{
    [Fact]
    public void DecimalPow10_6_ReturnsOneMillion()
    {
        Assert.Equal(1_000_000m, TokenConverter.DecimalPow10(6));
    }

    [Fact]
    public void DecimalPow10_18_ReturnsCorrect()
    {
        Assert.Equal(1_000_000_000_000_000_000m, TokenConverter.DecimalPow10(18));
    }

    [Fact]
    public void ToTokenAmount_Usdt_6Decimals()
    {
        // 20200000 raw = 20.2 USDT (6 decimals)
        Assert.Equal(20.2m, TokenConverter.ToTokenAmount(new BigInteger(20_200_000), 6));
    }

    [Fact]
    public void ToTokenAmount_Eth_18Decimals()
    {
        // 10^18 raw = 1.0 token (18 decimals)
        var raw = BigInteger.Pow(10, 18);
        Assert.Equal(1.0m, TokenConverter.ToTokenAmount(raw, 18));
    }

    [Fact]
    public void ToRawAmount_Roundtrip()
    {
        var original = 123.456789m;
        var raw = TokenConverter.ToRawAmount(original, 6);
        var back = TokenConverter.ToTokenAmount(raw, 6);
        Assert.Equal(original, back);
    }

    [Fact]
    public void ToTokenAmount_ZeroDecimals_ReturnsSameValue()
    {
        Assert.Equal(100m, TokenConverter.ToTokenAmount(new BigInteger(100), 0));
    }
}
