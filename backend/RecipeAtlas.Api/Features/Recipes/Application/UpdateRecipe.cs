using System.Data;
using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Features.Recipes.Mapping;

namespace Recipoke.Api.Features.Recipes.Application;

public sealed class UpdateRecipe(RecipeDb db, CookbookAccess access)
{
    public async Task<SaveRecipeResult> ExecuteAsync(Guid cookbookId, Guid id, RecipeInput input, CancellationToken ct)
    {
        await using var tx = await WriteTransaction.BeginAsync(db, ct);
        await access.RequireAsync(cookbookId, CookbookPermission.Write, ct);
        var recipe = await db.Recipes.Include(x => x.Ingredients).Include(x => x.Steps)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id && x.CookbookId == cookbookId, ct);
        if (recipe is null) throw new OperationFailure(404, "This recipe is no longer available.", "resource_unavailable");
        var expected = new VersionPrecondition(input.ExpectedVersion);
        expected.Require();
        var errors = RecipeValidation.Check(input);
        if (errors.Count > 0) return new(null, errors);
        ConcurrencyWrites.Advance(db.Entry(recipe).Property(x => x.Version), expected);
        RecipeMapping.Apply(recipe, input);
        await ConcurrencyWrites.SaveAsync(db, expected, () => RecipeAvailability.RequireWriteAsync(db, access, cookbookId, id, ct), ct);
        var hasImage = await db.Images.AnyAsync(x => x.RecipeId == id, ct);
        await tx.CommitAsync(ct);
        return new(RecipeMapping.ToResponse(recipe, hasImage), errors);
    }
}
