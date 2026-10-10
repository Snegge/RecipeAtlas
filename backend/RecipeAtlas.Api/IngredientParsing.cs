using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RecipeAtlas.Api;

public sealed record IngredientDraft(string Name, string Quantity, string Unit, string? Note,
    string OriginalText, bool RequiresReview, string? ReviewReason);
public sealed record MeasurementUnit(string Code, string[] Aliases, string Factor, string ReviewReason);
public sealed record MeasurementDefinitions(MeasurementUnit[] Definitions, string[] UnresolvedAliases);

public static class UnitNormalization
{
    private static readonly MeasurementDefinitions Data = JsonSerializer.Deserialize<MeasurementDefinitions>(
        Assembly.GetExecutingAssembly().GetManifestResourceStream("RecipeAtlas.Api.MeasurementUnits.json")!,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    private static readonly Dictionary<string, MeasurementUnit> Aliases = Data.Definitions
        .SelectMany(u => u.Aliases.Select(a => (a, u))).ToDictionary(x => x.a, x => x.u, StringComparer.OrdinalIgnoreCase);
    private static readonly string[] AllAliases = Aliases.Keys.Concat(Data.UnresolvedAliases)
        .OrderByDescending(a => a.Length).ToArray();
    public static (string Text, MeasurementUnit? Definition)? Match(string text)
    {
        foreach (var alias in AllAliases)
        {
            if (!text.StartsWith(alias, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = text[alias.Length..];
            if (rest.StartsWith('.')) rest = rest[1..];
            if (rest.Length == 0 || char.IsWhiteSpace(rest[0]))
                return (text[..(text.Length - rest.Length)], Aliases.GetValueOrDefault(alias));
        }
        // Explicit systems that we cannot resolve must never fall back to piece.
        if (Regex.IsMatch(text, @"^(?:us|u\.s\.|uk|imperial|metric|british)\b", RegexOptions.IgnoreCase))
            return (text.Split(' ')[0], null);
        return null;
    }
}

public static class IngredientParsing
{
    public static IngredientDraft ParseDraft(string input)
    {
        var original = input;
        var text = Regex.Replace(input.Trim(), @"\s+", " ");
        string? note = null;
        IngredientDraft Unresolved(string reason, string? name = null, string quantity = "", string unit = "") =>
            new(name ?? original.Trim(), quantity, unit, note, original, true, reason);
        if (text.Length == 0 || input.Length > 600) return Unresolved("Enter an ingredient line up to 600 characters.");
        var noteMatch = Regex.Match(text, @"\s+\(([^()]*)\)$");
        // Parenthesized unit system, e.g. cups (US), belongs to the unit, not to a note.
        if (noteMatch.Success && !Regex.IsMatch(noteMatch.Groups[1].Value, @"^(us|uk|imperial|metric)$", RegexOptions.IgnoreCase))
        {
            note = string.IsNullOrWhiteSpace(noteMatch.Groups[1].Value) ? null : noteMatch.Groups[1].Value.Trim();
            text = text[..noteMatch.Index].Trim();
        }
        var taste = Regex.Match(text, @"^(?:(?:to taste|nach geschmack)\s+(.+)|(.+?)\s+(?:to taste|nach geschmack))$", RegexOptions.IgnoreCase);
        if (taste.Success)
        {
            var name = taste.Groups[1].Success ? taste.Groups[1].Value : taste.Groups[2].Value;
            if (Regex.IsMatch(QuantityText.Expand(name), @"^[\d.,+\-]")) return Unresolved("Do not include an amount with to taste.");
            return name.Length <= 200 && note?.Length is not > 300
                ? new(name, "", "toTaste", note, original, false, null) : Unresolved("Name or note is too long.");
        }
        text = QuantityText.Expand(text);
        var amount = Regex.Match(text, "^(" + QuantityText.Pattern + @")\s*(.*)$", RegexOptions.IgnoreCase);
        if (!amount.Success) return Unresolved("Amount is missing or malformed; review the original ingredient.");
        var quantity = QuantityText.Parse(amount.Groups[1].Value);
        var remainder = amount.Groups[2].Value.Trim();
        if (quantity is null || remainder.Length == 0 || Regex.IsMatch(remainder, @"^(?:[\d.,/–—−+\-×*%]|[eE][+-]?\d|to\b|bis\b|x\s*\d)"))
            return Unresolved("Quantity or ingredient name is invalid.");
        var match = UnitNormalization.Match(remainder);
        var unit = "piece";
        string? review = null;
        var normalized = quantity.Canonical;
        if (match is not null)
        {
            remainder = remainder[match.Value.Text.Length..].Trim();
            if (match.Value.Definition is not { } definition)
            {
                unit = "";
                review = "Unit or measurement system is unresolved; choose a supported unit.";
            }
            else
            {
                unit = definition.Code;
                review = string.IsNullOrEmpty(definition.ReviewReason) ? null : definition.ReviewReason;
                if (definition.Factor != "1")
                {
                    var converted = QuantityText.Convert(quantity, definition.Factor);
                    if (converted is null) return Unresolved("Converted amount is outside supported bounds.", remainder);
                    normalized = converted;
                }
            }
        }

        // Package size is metadata, never a second quantity or a universal density.
        var package = Regex.Match(remainder, @"^(?:à|a|je|each)\s*(" + QuantityText.Pattern + @")\s*(g|kg|ml|l)\s+(.+)$", RegexOptions.IgnoreCase);
        if (package.Success)
        {
            note = Join(note, "à " + package.Groups[1].Value + " " + package.Groups[2].Value);
            remainder = package.Groups[3].Value.Trim();
        }
        var trailingPackage = Regex.Match(remainder, @"\s+(?:à|je|each)\s*" + QuantityText.Pattern + @"\s*(?:g|kg|ml|l)$", RegexOptions.IgnoreCase);
        if (trailingPackage.Success)
        {
            note = Join(note, trailingPackage.Value.Trim()); remainder = remainder[..trailingPackage.Index].Trim();
        }
        var comma = remainder.IndexOf(',');
        if (comma >= 0) { note = Join(note, remainder[(comma + 1)..].Trim()); remainder = remainder[..comma].Trim(); }
        if (remainder.Length is < 1 or > 200 || note?.Length > 300 || Regex.IsMatch(remainder, @"^[\d×+\-]"))
            return Unresolved("Ingredient name or note is invalid.");
        return new(remainder, normalized, unit, note, original, review is not null, review);
    }
    private static string? Join(string? first, string second) => string.IsNullOrEmpty(first) ? second : first + "; " + second;
}
