namespace Recipoke.Domain.Recipes;

public sealed class Recipe
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CookbookId { get; set; }
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

