using Microsoft.EntityFrameworkCore;

namespace Recipoke.Api.Features.Recipes.Application;

public static class RecipeAvailability
{
    public static async Task RequireWriteAsync(RecipeDb db, CookbookAccess access, Guid cookbookId, Guid id, CancellationToken ct)
    {
        await access.RequireAsync(cookbookId, CookbookPermission.Write, ct);
        if (!await db.Recipes.AsNoTracking().AnyAsync(x => x.Id == id && x.CookbookId == cookbookId, ct))
            throw new OperationFailure(404, "This recipe is no longer available.", "resource_unavailable");
    }
}
