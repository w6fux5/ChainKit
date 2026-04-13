using System.Numerics;
using ChainKit.Core.Converters;

namespace ChainKit.Tron.Crypto;

/// <summary>
/// Tron-specific unit conversions: Sun ↔ TRX.
/// Token amount conversions delegate to ChainKit.Core.Converters.TokenConverter.
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
        => TokenConverter.ToTokenAmount(rawAmount, decimals);

    /// <summary>
    /// 將人類可讀金額轉換為代幣原始值。
    /// 例如：amount=1.0, decimals=6 → 1000000
    /// </summary>
    public static BigInteger ToRawAmount(decimal amount, int decimals)
        => TokenConverter.ToRawAmount(amount, decimals);

    /// <summary>
    /// Computes 10^exp using decimal multiplication to avoid double precision loss from Math.Pow.
    /// </summary>
    internal static decimal DecimalPow10(int exp)
        => TokenConverter.DecimalPow10(exp);
}
