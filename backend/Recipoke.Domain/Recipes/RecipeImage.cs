namespace Recipoke.Domain.Recipes;

public sealed class RecipeImage
{
    public Guid RecipeId { get; set; }
    public string ContentType { get; set; } = "";
    public byte[] Bytes { get; set; } = [];
}

