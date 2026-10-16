using System.Data;
using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Features.Recipes.Mapping;

namespace Recipoke.Api.Features.Recipes.Application;

public sealed class DeleteRecipe(RecipeDb db, CookbookAccess access)
{
    public async Task<bool> ExecuteAsync(Guid cookbookId, Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await access.RequireAsync(cookbookId, CookbookPermission.Write, ct);
        var deleted = await db.Recipes.Where(x => x.Id == id && x.CookbookId == cookbookId).ExecuteDeleteAsync(ct) != 0;
        await tx.CommitAsync(ct); return deleted;
    }
}
