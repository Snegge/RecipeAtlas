namespace Recipoke.Domain.Quantities;

public static class QuantityConversion
{
    public static string? Convert(ParsedQuantity quantity, string factor)
    {
        var converted = quantity.Values.Select(v => v.Multiply(Rational.FromDecimal(factor))).ToArray();
        if (converted.Any(v => !QuantityText.InBounds(v))) return null;
        var result = string.Join('-', converted.Select(v => v.Format()));
        return QuantityText.Parse(result) is null ? null : result;
    }
}
