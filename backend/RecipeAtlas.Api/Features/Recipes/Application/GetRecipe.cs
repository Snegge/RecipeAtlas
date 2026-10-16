using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Features.Recipes.Mapping;

namespace Recipoke.Api.Features.Recipes.Application;

public sealed class GetRecipe(RecipeDb db, CookbookAccess access)
{
    public async Task<RecipeResponse?> ExecuteAsync(Guid cookbookId, Guid id, CancellationToken ct)
    {
        await access.RequireAsync(cookbookId, CookbookPermission.Read, ct);
        var recipe = await db.Recipes.Include(x => x.Ingredients).Include(x => x.Steps)
            .AsSplitQuery().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.CookbookId == cookbookId, ct);
        if (recipe is null) return null;
        var hasImage = await db.Images.AnyAsync(x => x.RecipeId == id, ct);
        return RecipeMapping.ToResponse(recipe, hasImage);
    }
}
