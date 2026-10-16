namespace Recipoke.Domain.Quantities;

public sealed record UnitDefinition(string Code, string Label, decimal? Milliliters = null);

public static class Units
{
    // Stable API codes. Frontends may translate labels without changing stored values.
    // tsp and tbsp are metric: 5 ml and 15 ml. Cups are intentionally excluded.
    public static readonly UnitDefinition[] All =
    [
        new("g", "gram"), new("kg", "kilogram"),
        new("ml", "milliliter", 1), new("l", "liter", 1000),
        new("tsp", "metric teaspoon", 5), new("tbsp", "metric tablespoon", 15),
        new("piece", "piece"), new("pinch", "pinch"), new("toTaste", "to taste"),
        new("pack", "package"),
        new("can", "can"), new("jar", "jar"),
    ];
    public static readonly HashSet<string> Codes = All.Select(x => x.Code).ToHashSet();
}
