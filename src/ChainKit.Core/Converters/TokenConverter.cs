using System.Numerics;

namespace ChainKit.Core.Converters;

/// <summary>
/// Chain-agnostic token amount conversion utilities.
/// Uses decimal loop multiplication (not Math.Pow) to avoid double precision loss.
/// </summary>
public static class TokenConverter
{
    /// <summary>
    /// Computes 10^exp using decimal multiplication to avoid double precision loss from Math.Pow.
    /// </summary>
    public static decimal DecimalPow10(int exp)
    {
        var result = 1m;
        for (int i = 0; i < exp; i++) result *= 10;
        return result;
    }

    /// <summary>
    /// Converts raw token amount to human-readable amount (divide by 10^decimals).
    /// Throws <see cref="OverflowException"/> if the value exceeds decimal range (~7.9 × 10^28).
    /// Example: rawAmount=1000000, decimals=6 → 1.0
    /// </summary>
    public static decimal ToTokenAmount(BigInteger rawAmount, int decimals)
    {
        if (decimals <= 0) return (decimal)rawAmount;
        var divisor = BigInteger.Pow(10, decimals);
        var wholePart = BigInteger.DivRem(rawAmount, divisor, out var remainder);
        return (decimal)wholePart + (decimal)remainder / (decimal)divisor;
    }

    /// <summary>
    /// Safe version of <see cref="ToTokenAmount"/>. Returns null if the value exceeds decimal range.
    /// </summary>
    public static decimal? TryToTokenAmount(BigInteger rawAmount, int decimals)
    {
        try { return ToTokenAmount(rawAmount, decimals); }
        catch (OverflowException) { return null; }
    }

    /// <summary>
    /// Converts human-readable amount to raw token amount (multiply by 10^decimals).
    /// Example: amount=1.0, decimals=6 → 1000000
    /// </summary>
    public static BigInteger ToRawAmount(decimal amount, int decimals)
    {
        if (decimals <= 0) return new BigInteger(amount);
        var multiplier = DecimalPow10(decimals);
        return new BigInteger(amount * multiplier);
    }
}
