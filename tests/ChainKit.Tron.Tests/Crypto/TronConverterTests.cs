using System.Numerics;
using ChainKit.Tron.Crypto;
using Xunit;

namespace ChainKit.Tron.Tests.Crypto;

public class TronConverterTests
{
    [Fact]
    public void SunToTrx_OneMillion_ReturnsOne()
    {
        Assert.Equal(1.0m, TronConverter.SunToTrx(1_000_000));
    }

    [Fact]
    public void SunToTrx_Zero_ReturnsZero()
    {
        Assert.Equal(0m, TronConverter.SunToTrx(0));
    }

    [Fact]
    public void SunToTrx_SmallAmount_ReturnsFraction()
    {
        Assert.Equal(0.000001m, TronConverter.SunToTrx(1));
    }

    [Fact]
    public void TrxToSun_One_ReturnsOneMillion()
    {
        Assert.Equal(1_000_000, TronConverter.TrxToSun(1.0m));
    }

    [Fact]
    public void TrxToSun_Fractional_ReturnsCorrect()
    {
        Assert.Equal(1_500_000, TronConverter.TrxToSun(1.5m));
    }

    [Fact]
    public void TrxToSun_Overflow_Throws()
    {
        Assert.Throws<OverflowException>(() => TronConverter.TrxToSun(decimal.MaxValue));
    }

    [Fact]
    public void Roundtrip_SunTrxSun()
    {
        Assert.Equal(12_345_678, TronConverter.TrxToSun(TronConverter.SunToTrx(12_345_678)));
    }

    [Fact]
    public void ToTokenAmount_Usdt_6Decimals()
    {
        // 20200000 raw = 20.2 USDT (6 decimals)
        Assert.Equal(20.2m, TronConverter.ToTokenAmount(new BigInteger(20_200_000), 6));
    }

    [Fact]
    public void ToTokenAmount_18Decimals()
    {
        // 1000000000000000000 raw = 1.0 token (18 decimals)
        var raw = BigInteger.Pow(10, 18);
        Assert.Equal(1.0m, TronConverter.ToTokenAmount(raw, 18));
    }

    [Fact]
    public void ToTokenAmount_ZeroDecimals_ReturnsRaw()
    {
        Assert.Equal(100m, TronConverter.ToTokenAmount(new BigInteger(100), 0));
    }

    [Fact]
    public void ToRawAmount_Usdt_6Decimals()
    {
        Assert.Equal(new BigInteger(20_200_000), TronConverter.ToRawAmount(20.2m, 6));
    }

    [Fact]
    public void ToRawAmount_ZeroDecimals_ReturnsAsIs()
    {
        Assert.Equal(new BigInteger(100), TronConverter.ToRawAmount(100m, 0));
    }

    [Fact]
    public void Roundtrip_TokenAmount()
    {
        var original = 123.456789m;
        var raw = TronConverter.ToRawAmount(original, 6);
        var back = TronConverter.ToTokenAmount(raw, 6);
        Assert.Equal(original, back);
    }
}
