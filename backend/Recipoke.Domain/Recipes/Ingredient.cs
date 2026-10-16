namespace Recipoke.Domain.Recipes;

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

