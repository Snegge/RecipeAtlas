using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Features.Recipes.Mapping;

namespace Recipoke.Api.Features.Recipes.Application;

public sealed class UpdateRecipe(RecipeDb db)
{
    public async Task<SaveRecipeResult> ExecuteAsync(Guid id, RecipeInput input, CancellationToken ct)
    {
        var errors = RecipeValidation.Check(input);
        if (errors.Count > 0) return new(null, errors);
        var recipe = await db.Recipes.Include(x => x.Ingredients).Include(x => x.Steps)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (recipe is null) return new(null, errors);
        RecipeMapping.Apply(recipe, input);
        await db.SaveChangesAsync(ct);
        var hasImage = await db.Images.AnyAsync(x => x.RecipeId == id, ct);
        return new(RecipeMapping.ToResponse(recipe, hasImage), errors);
    }
}
