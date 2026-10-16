using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Recipoke.Domain.Quantities;

public sealed record MeasurementUnit(string Code, string[] Aliases, string Factor, string ReviewReason);
public sealed record MeasurementDefinitions(MeasurementUnit[] Definitions, string[] UnresolvedAliases);

public static class UnitNormalization
{
    private static readonly MeasurementDefinitions Data = JsonSerializer.Deserialize<MeasurementDefinitions>(
        Assembly.GetExecutingAssembly().GetManifestResourceStream("Recipoke.Domain.Quantities.MeasurementUnits.json")!,
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

