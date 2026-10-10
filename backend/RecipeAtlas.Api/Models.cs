namespace RecipeAtlas.Api;

public sealed class Recipe
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? SourceUrl { get; set; }
    public int Servings { get; set; } = 2;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<Ingredient> Ingredients { get; set; } = [];
    public List<RecipeStep> Steps { get; set; } = [];
    public RecipeImage? Image { get; set; }
}

public sealed class Ingredient
{
    public int Id { get; set; }
    public Guid RecipeId { get; set; }
    public int Position { get; set; }
    public string Name { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string Unit { get; set; } = "g";
    public string? Note { get; set; }
}

public sealed class RecipeStep
{
    public int Id { get; set; }
    public Guid RecipeId { get; set; }
    public int Position { get; set; }
    public string Instruction { get; set; } = "";
}

public sealed class RecipeImage
{
    public Guid RecipeId { get; set; }
    public string ContentType { get; set; } = "";
    public byte[] Bytes { get; set; } = [];
}

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
