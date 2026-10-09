using System.Globalization;
using System.Text.RegularExpressions;

namespace RecipeAtlas.Api.Features.RecipeImport;

public static class IngredientTextParser
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private static readonly Regex QuantityPattern = new(
        @"^(?<quantity>\d+\s+\d+/\d+|\d+/\d+|\d+(?:[.,]\d+)?)\s*(?<rest>.+)$",
        RegexOptions.CultureInvariant,
        Timeout);

    private static readonly Regex UnitPattern = new(
        @"^(?<unit>[^\s]+)\s+(?<name>.+)$",
        RegexOptions.CultureInvariant,
        Timeout);

    private static readonly Dictionary<string, string> UnitAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["g"] = "g",
            ["gr"] = "g",
            ["gram"] = "g",
            ["grams"] = "g",
            ["gramm"] = "g",

            ["kg"] = "kg",
            ["kilogram"] = "kg",
            ["kilograms"] = "kg",
            ["kilogramm"] = "kg",

            ["ml"] = "ml",
            ["milliliter"] = "ml",
            ["millilitre"] = "ml",

            ["l"] = "l",
            ["liter"] = "l",
            ["litre"] = "l",

            ["tl"] = "tsp",
            ["teelöffel"] = "tsp",
            ["tsp"] = "tsp",
            ["teaspoon"] = "tsp",
            ["teaspoons"] = "tsp",

            ["el"] = "tbsp",
            ["esslöffel"] = "tbsp",
            ["eßlöffel"] = "tbsp",
            ["tbsp"] = "tbsp",
            ["tablespoon"] = "tbsp",
            ["tablespoons"] = "tbsp",

            ["stück"] = "piece",
            ["stk"] = "piece",
            ["piece"] = "piece",
            ["pieces"] = "piece",

            ["prise"] = "pinch",
            ["prisen"] = "pinch",
            ["pinch"] = "pinch",
            ["pinches"] = "pinch",

            ["pck"] = "pack",
            ["pckg"] = "pack",
            ["päckchen"] = "pack",
            ["packung"] = "pack",
            ["packungen"] = "pack",
            ["pack"] = "pack",
            ["packs"] = "pack",
            ["packet"] = "pack",
            ["packets"] = "pack",

            ["dose"] = "can",
            ["dosen"] = "can",
            ["dose/n"] = "can",
            ["can"] = "can",
            ["cans"] = "can",
            ["tin"] = "can",
            ["tins"] = "can"
        };

    public static ImportedIngredient Parse(string originalText)
    {
        var text = NormalizeFractions(originalText.Trim());

        var match = QuantityPattern.Match(text);

        if (!match.Success ||
            !TryReadQuantity(match.Groups["quantity"].Value, out var quantity) ||
            quantity is <= 0 or > 100000)
        {
            return Unparsed(originalText);
        }

        var roundedQuantity = decimal.Round(
            quantity,
            3,
            MidpointRounding.AwayFromZero);

        if (roundedQuantity <= 0)
            return Unparsed(originalText);

        var rest = match.Groups["rest"].Value.Trim();
        var unitMatch = UnitPattern.Match(rest);

        if (unitMatch.Success)
        {
            var alias = unitMatch.Groups["unit"].Value.TrimEnd('.');

            if (UnitAliases.TryGetValue(alias, out var unit))
            {
                var name = unitMatch.Groups["name"].Value.Trim();
                var (cleanName, note) = ExtractTrailingNote(name);

                var needsReview =
                    roundedQuantity != quantity ||
                    RequiresSpoonReview(alias) ||
                    cleanName.Length > 200 ||
                    note?.Length > 300;

                return new ImportedIngredient(
                    cleanName,
                    roundedQuantity,
                    unit,
                    note,
                    originalText,
                    needsReview);
            }
        }

        // Recognize a narrow set of countable ingredients.
        // Do not assume that an unknown token such as "cup" means pieces.
        if (Regex.IsMatch(
                rest,
                @"^(?:ei(?:\(er\)|er)?|eggs?)(?=$|[\s,;(])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                Timeout))
        {
            var (name, note) = ExtractTrailingNote(rest);

            return new ImportedIngredient(
                name,
                roundedQuantity,
                "piece",
                note,
                originalText,
                roundedQuantity != quantity ||
                name.Length > 200 ||
                note?.Length > 300);
        }

        return Unparsed(originalText);
    }

    private static ImportedIngredient Unparsed(string text)
    {
        return new ImportedIngredient(
            Name: text,
            Quantity: null,
            Unit: null,
            Note: null,
            OriginalText: text,
            RequiresReview: true);
    }

    private static bool RequiresSpoonReview(string alias)
    {
        return alias.ToLowerInvariant() is
            "tsp" or "teaspoon" or "teaspoons" or
            "tbsp" or "tablespoon" or "tablespoons";
    }

    private static (string Name, string? Note) ExtractTrailingNote(string text)
    {
        // Only split a final parenthesis preceded by whitespace.
        // This preserves words such as "Mandarine(n)" and "Ei(er)".
        var match = Regex.Match(
            text,
            @"^(?<name>.+?)\s+\((?<note>[^()]*)\)$",
            RegexOptions.CultureInvariant,
            Timeout);

        if (!match.Success)
            return (text, null);

        var name = match.Groups["name"].Value.Trim();
        var note = match.Groups["note"].Value.Trim();

        return (name, note.Length == 0 ? null : note);
    }

    private static string NormalizeFractions(string text)
    {
        return text
            .Replace("½", " 1/2")
            .Replace("¼", " 1/4")
            .Replace("¾", " 3/4")
            .Replace("⅓", " 1/3")
            .Replace("⅔", " 2/3")
            .Replace("⅛", " 1/8")
            .Replace("⅜", " 3/8")
            .Replace("⅝", " 5/8")
            .Replace("⅞", " 7/8")
            .Trim();
    }

    private static bool TryReadQuantity(string text, out decimal quantity)
    {
        quantity = 0;

        foreach (var part in text.Split(
                     ' ',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            decimal value;

            if (part.Contains('/'))
            {
                var fraction = part.Split('/');

                if (fraction.Length != 2 ||
                    !TryDecimal(fraction[0], out var numerator) ||
                    !TryDecimal(fraction[1], out var denominator) ||
                    denominator <= 0)
                {
                    return false;
                }

                value = numerator / denominator;
            }
            else if (!TryDecimal(part, out value))
            {
                return false;
            }

            // Match the bounds enforced by RecipeValidation.
            if (value < 0 || value > 100000 - quantity)
                return false;

            quantity += value;
        }

        return true;
    }

    private static bool TryDecimal(string text, out decimal value)
    {
        return decimal.TryParse(
            text.Replace(',', '.'),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value);
    }
}