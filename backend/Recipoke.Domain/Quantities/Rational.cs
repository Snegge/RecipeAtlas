using System.Globalization;
using System.Numerics;

namespace Recipoke.Domain.Quantities;

public readonly record struct Rational(BigInteger Numerator, BigInteger Denominator)
{
    public static Rational FromDecimal(string value)
    {
        var parts = value.Split('.');
        return new(BigInteger.Parse(string.Concat(parts), CultureInfo.InvariantCulture),
            BigInteger.Pow(10, parts.Length == 2 ? parts[1].Length : 0));
    }
    public Rational Multiply(Rational other) => new(Numerator * other.Numerator, Denominator * other.Denominator);
    public int Compare(Rational other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    public string Format(int places = 3)
    {
        var scale = BigInteger.Pow(10, places);
        var rounded = BigInteger.DivRem(Numerator * scale, Denominator, out var remainder);
        if (remainder * 2 >= Denominator) rounded++;
        // Callers reject conversions outside storage bounds. Never display a positive value as zero.
        if (rounded.IsZero && Numerator > 0) return $"{Numerator}/{Denominator}";
        var digits = rounded.ToString(CultureInfo.InvariantCulture).PadLeft(places + 1, '0');
        return places == 0 ? digits : (digits[..^places] + "." + digits[^places..]).TrimEnd('0').TrimEnd('.');
    }
}

