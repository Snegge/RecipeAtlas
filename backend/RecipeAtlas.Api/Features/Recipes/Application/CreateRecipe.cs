using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Features.Recipes.Mapping;

namespace Recipoke.Api.Features.Recipes.Application;

// Each write operation owns one SaveChanges transaction; callers must not commit again.
public sealed class CreateRecipe(RecipeDb db)
{
    public async Task<SaveRecipeResult> ExecuteAsync(RecipeInput input, CancellationToken ct)
    {
        var errors = RecipeValidation.Check(input);
        if (errors.Count > 0) return new(null, errors);
        var recipe = new Recipe();
        RecipeMapping.Apply(recipe, input);
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync(ct);
        return new(RecipeMapping.ToResponse(recipe, false), errors);
    }
}
