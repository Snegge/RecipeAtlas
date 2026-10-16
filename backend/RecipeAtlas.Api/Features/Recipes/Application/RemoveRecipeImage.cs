using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Features.Recipes.Mapping;

namespace Recipoke.Api.Features.Recipes.Application;

public sealed class RemoveRecipeImage(RecipeDb db)
{
    public async Task<bool> ExecuteAsync(Guid id, CancellationToken ct)
    {
        var recipe = await db.Recipes.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (recipe is null) return false;
        var image = await db.Images.SingleOrDefaultAsync(x => x.RecipeId == id, ct);
        if (image is not null)
        {
            db.Images.Remove(image);
            recipe.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return true;
    }
}
