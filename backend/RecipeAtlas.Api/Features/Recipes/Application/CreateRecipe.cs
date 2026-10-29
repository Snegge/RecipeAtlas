using System.Data;
using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Features.Recipes.Mapping;

namespace Recipoke.Api.Features.Recipes.Application;

// The operation owns the transaction from current permission check through SaveChanges and commit.
public sealed class CreateRecipe(RecipeDb db, CookbookAccess access)
{
    public async Task<SaveRecipeResult> ExecuteAsync(Guid cookbookId, RecipeInput input, CancellationToken ct)
    {
        await using var tx = await WriteTransaction.BeginAsync(db, ct);
        await access.RequireAsync(cookbookId, CookbookPermission.Write, ct);
        var errors = RecipeValidation.Check(input);
        if (errors.Count > 0) return new(null, errors);
        var recipe = new Recipe { CookbookId = cookbookId };
        RecipeMapping.Apply(recipe, input);
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(RecipeMapping.ToResponse(recipe, false), errors);
    }
}
