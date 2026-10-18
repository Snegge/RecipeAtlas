using System.Data;
using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Features.Recipes.Mapping;

namespace Recipoke.Api.Features.Recipes.Application;

public sealed class RemoveRecipeImage(RecipeDb db, CookbookAccess access)
{
    public async Task<bool> ExecuteAsync(Guid cookbookId, Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await access.RequireAsync(cookbookId, CookbookPermission.Write, ct);
        var recipe = await db.Recipes.SingleOrDefaultAsync(x => x.Id == id && x.CookbookId == cookbookId, ct);
        if (recipe is null) return false;
        var image = await db.Images.SingleOrDefaultAsync(x => x.RecipeId == id, ct);
        if (image is not null)
        {
            db.Images.Remove(image);
            recipe.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return true;
    }
}
