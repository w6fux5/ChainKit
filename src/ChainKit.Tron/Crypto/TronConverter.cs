using System.Numerics;

namespace ChainKit.Tron.Crypto;

/// <summary>
/// 單位轉換工具：Sun ↔ TRX、raw token amount ↔ human-readable amount。
/// </summary>
public static class TronConverter
{
    private const long SunPerTrx = 1_000_000;

    /// <summary>
    /// Sun 轉換為 TRX。1 TRX = 1,000,000 Sun。
    /// </summary>
    public static decimal SunToTrx(long sun) => (decimal)sun / SunPerTrx;

    /// <summary>
    /// TRX 轉換為 Sun。1 TRX = 1,000,000 Sun。
    /// </summary>
    /// <exception cref="OverflowException">金額超出 long 範圍。</exception>
    public static long TrxToSun(decimal trx) => checked((long)(trx * SunPerTrx));

    /// <summary>
    /// 將代幣原始值轉換為人類可讀金額。
    /// 例如：rawAmount=1000000, decimals=6 → 1.0
    /// </summary>
    public static decimal ToTokenAmount(BigInteger rawAmount, int decimals)
    {
        if (decimals <= 0) return (decimal)rawAmount;
        var divisor = BigInteger.Pow(10, decimals);
        var wholePart = BigInteger.DivRem(rawAmount, divisor, out var remainder);
        return (decimal)wholePart + (decimal)remainder / (decimal)divisor;
    }

    /// <summary>
    /// 將人類可讀金額轉換為代幣原始值。
    /// 例如：amount=1.0, decimals=6 → 1000000
    /// </summary>
    public static BigInteger ToRawAmount(decimal amount, int decimals)
    {
        if (decimals <= 0) return new BigInteger(amount);
        var multiplier = DecimalPow10(decimals);
        return new BigInteger(amount * multiplier);
    }

    /// <summary>
    /// Computes 10^exp using decimal multiplication to avoid double precision loss from Math.Pow.
    /// </summary>
    internal static decimal DecimalPow10(int exp)
    {
        var result = 1m;
        for (int i = 0; i < exp; i++) result *= 10;
        return result;
    }
}
