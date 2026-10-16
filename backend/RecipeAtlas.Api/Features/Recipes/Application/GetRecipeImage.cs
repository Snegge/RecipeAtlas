using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Features.Recipes.Mapping;

namespace Recipoke.Api.Features.Recipes.Application;

public sealed record RecipeImageContent(byte[] Bytes, string ContentType);
public sealed class GetRecipeImage(RecipeDb db)
{
    public Task<RecipeImageContent?> ExecuteAsync(Guid id, CancellationToken ct) =>
        db.Images.AsNoTracking().Where(x => x.RecipeId == id)
            .Select(x => new RecipeImageContent(x.Bytes, x.ContentType)).SingleOrDefaultAsync(ct);
}
