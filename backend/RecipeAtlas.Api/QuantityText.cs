using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace RecipeAtlas.Api;

// Exact rationals avoid losing fractional precision or accumulating rounding error.
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

public sealed record ParsedQuantity(string Canonical, IReadOnlyList<Rational> Values);

public static class QuantityText
{
    public const string Atom = @"(?:[0-9]+\s+[0-9]+\s*/\s*[0-9]+|[0-9]+\s*/\s*[0-9]+|(?:[0-9]+(?:[.,][0-9]+)?|[.,][0-9]+))";
    public const string Pattern = Atom + @"(?:\s*(?:[-–—]|\bto\b|\bbis\b)\s*" + Atom + @")?";
    private static readonly Dictionary<char, string> Fractions = new()
    {
        ['½'] = "1/2",
        ['¼'] = "1/4",
        ['¾'] = "3/4",
        ['⅐'] = "1/7",
        ['⅑'] = "1/9",
        ['⅒'] = "1/10",
        ['⅓'] = "1/3",
        ['⅔'] = "2/3",
        ['⅕'] = "1/5",
        ['⅖'] = "2/5",
        ['⅗'] = "3/5",
        ['⅘'] = "4/5",
        ['⅙'] = "1/6",
        ['⅚'] = "5/6",
        ['⅛'] = "1/8",
        ['⅜'] = "3/8",
        ['⅝'] = "5/8",
        ['⅞'] = "7/8"
    };
    public static string Expand(string text) => Regex.Replace(text.Replace('⁄', '/'), @"[½¼¾⅐⅑⅒⅓⅔⅕⅖⅗⅘⅙⅚⅛⅜⅝⅞]",
        m => (m.Index > 0 && char.IsAsciiDigit(text[m.Index - 1]) ? " " : "") + Fractions[m.Value[0]] +
            (m.Index + 1 < text.Length && char.IsAsciiDigit(text[m.Index + 1]) ? " " : ""));

    public static ParsedQuantity? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 64) return null;
        var text = Expand(input.Trim());
        if (!Regex.IsMatch(text, "^" + Pattern + "$", RegexOptions.IgnoreCase)) return null;
        var endpoints = Regex.Split(text, @"\s*(?:[-–—]|\bto\b|\bbis\b)\s*", RegexOptions.IgnoreCase);
        var values = new List<Rational>();
        var canonical = new List<string>();
        foreach (var endpoint in endpoints)
        {
            Rational value;
            string normalized;
            var fraction = Regex.Match(endpoint.Trim(), @"^(?:([0-9]+)\s+)?([0-9]+)\s*/\s*([0-9]+)$");
            if (fraction.Success)
            {
                var whole = BigInteger.Parse(fraction.Groups[1].Success ? fraction.Groups[1].Value : "0");
                var numerator = BigInteger.Parse(fraction.Groups[2].Value);
                var denominator = BigInteger.Parse(fraction.Groups[3].Value);
                if (denominator.IsZero || (fraction.Groups[1].Success && numerator >= denominator)) return null;
                value = new(whole * denominator + numerator, denominator);
                normalized = (fraction.Groups[1].Success && whole > 0 ? whole + " " : "") + numerator + "/" + denominator;
            }
            else
            {
                normalized = endpoint.Trim().Replace(',', '.');
                var parts = normalized.Split('.');
                normalized = (parts[0].TrimStart('0') is { Length: > 0 } digits ? digits : "0") +
                    (parts.Length == 2 && parts[1].TrimEnd('0') is { Length: > 0 } decimals ? "." + decimals : "");
                value = Rational.FromDecimal(normalized);
            }
            if (!InBounds(value)) return null;
            values.Add(value); canonical.Add(normalized);
        }
        if (values.Count == 2 && values[0].Compare(values[1]) > 0) return null;
        var result = string.Join('-', canonical);
        return result.Length <= 64 ? new(result, values) : null;
    }
    public static bool InBounds(Rational value) => value.Compare(new(1, 1000)) >= 0 && value.Compare(new(100000, 1)) <= 0;
    public static string? Convert(ParsedQuantity quantity, string factor)
    {
        var converted = quantity.Values.Select(v => v.Multiply(Rational.FromDecimal(factor))).ToArray();
        if (converted.Any(v => !InBounds(v))) return null;
        var result = string.Join('-', converted.Select(v => v.Format()));
        return Parse(result) is null ? null : result;
    }
}
