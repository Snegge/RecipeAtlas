using Recipoke.Api.Features.Recipes.Application;

namespace Recipoke.Api.Features.Recipes.Mapping;

public static class RecipeMapping
{
    public static RecipeResponse ToResponse(Recipe recipe, bool hasImage) => new(
        recipe.Id, recipe.Title, recipe.Description, recipe.SourceUrl, recipe.Servings,
        recipe.Ingredients.OrderBy(x => x.Position)
            .Select(x => new IngredientResponse(x.Name, x.Quantity, x.Unit, x.Note)).ToArray(),
        recipe.Steps.OrderBy(x => x.Position).Select(x => x.Instruction).ToArray(),
        hasImage ? $"/api/cookbooks/{recipe.CookbookId}/recipes/{recipe.Id}/image" : null,
        recipe.CreatedAtUtc, recipe.UpdatedAtUtc, recipe.Version);

    public static void Apply(Recipe recipe, RecipeInput input)
    {
        recipe.Title = input.Title!.Trim();
        recipe.Description = Clean(input.Description);
        recipe.SourceUrl = Clean(input.SourceUrl);
        recipe.Servings = input.Servings;
        recipe.UpdatedAtUtc = DateTime.UtcNow;
        recipe.Ingredients.Clear();
        recipe.Ingredients.AddRange(input.Ingredients!.Select((x, index) => new Ingredient
        {
            Name = x!.Name!.Trim(), Quantity = x.Unit == "toTaste" ? "" : QuantityText.Parse(x.Quantity!)!.Canonical, Unit = x.Unit!,
            Note = Clean(x.Note), Position = index
        }));
        recipe.Steps.Clear();
        recipe.Steps.AddRange(input.Steps!.Select((x, index) => new RecipeStep
        {
            Instruction = x!.Trim(), Position = index
        }));
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

