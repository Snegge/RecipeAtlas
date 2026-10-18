using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Features.Recipes.Mapping;

namespace Recipoke.Api.Features.Recipes.Application;

public sealed record RecipeImageContent(byte[] Bytes, string ContentType);
public sealed class GetRecipeImage(RecipeDb db, CookbookAccess access)
{
    public async Task<RecipeImageContent?> ExecuteAsync(Guid cookbookId, Guid id, CancellationToken ct)
    {
        await access.RequireAsync(cookbookId, CookbookPermission.Read, ct);
        return await db.Images.AsNoTracking().Where(x => x.RecipeId == id && db.Recipes.Any(recipe => recipe.Id == x.RecipeId && recipe.CookbookId == cookbookId))
            .Select(x => new RecipeImageContent(x.Bytes, x.ContentType)).SingleOrDefaultAsync(ct);
    }
}
