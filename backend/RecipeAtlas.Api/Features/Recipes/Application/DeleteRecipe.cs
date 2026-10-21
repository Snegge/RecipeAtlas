using System.Data;
using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Features.Recipes.Mapping;

namespace Recipoke.Api.Features.Recipes.Application;

public sealed class DeleteRecipe(RecipeDb db, CookbookAccess access)
{
    public async Task<bool> ExecuteAsync(Guid cookbookId, Guid id, VersionPrecondition expected, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await access.RequireAsync(cookbookId, CookbookPermission.Write, ct);
        var recipe = await db.Recipes.SingleOrDefaultAsync(x => x.Id == id && x.CookbookId == cookbookId, ct);
        if (recipe is null) throw new OperationFailure(404, "This recipe is no longer available.", "resource_unavailable");
        ConcurrencyWrites.Advance(db.Entry(recipe).Property(x => x.Version), expected);
        db.Recipes.Remove(recipe);
        await ConcurrencyWrites.SaveAsync(db, expected, () => RecipeAvailability.RequireWriteAsync(db, access, cookbookId, id, ct), ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
