namespace Recipoke.Domain.Recipes;

public sealed class RecipeStep
{
    public int Id { get; set; }
    public Guid RecipeId { get; set; }
    public int Position { get; set; }
    public string Instruction { get; set; } = "";
}

