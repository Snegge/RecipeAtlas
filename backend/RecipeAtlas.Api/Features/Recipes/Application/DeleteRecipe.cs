using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Features.Recipes.Mapping;

namespace Recipoke.Api.Features.Recipes.Application;

public sealed class DeleteRecipe(RecipeDb db)
{
    // One SQL statement; database foreign keys atomically delete children and image.
    public async Task<bool> ExecuteAsync(Guid id, CancellationToken ct) =>
        await db.Recipes.Where(x => x.Id == id).ExecuteDeleteAsync(ct) != 0;
}
