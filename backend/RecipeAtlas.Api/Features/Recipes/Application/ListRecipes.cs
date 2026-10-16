using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Features.Recipes.Mapping;

namespace Recipoke.Api.Features.Recipes.Application;

public sealed class ListRecipes(RecipeDb db)
{
    public async Task<ListRecipesResult> ExecuteAsync(string? search, int? page, int? pageSize, CancellationToken ct)
    {
        var number = page ?? 1;
        var size = pageSize ?? 20;
        if (number is < 1 or > 100000 || size is < 1 or > 100 || search?.Length > 200)
            return new(null, "Use page 1..100000, pageSize 1..100, and search up to 200 characters.");
        var query = db.Recipes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var pattern = "%" + text + "%";
            query = query.Where(x => EF.Functions.Like(x.Title, pattern, "\\") ||
                x.Ingredients.Any(i => EF.Functions.Like(i.Name, pattern, "\\")));
        }
        var total = await query.CountAsync(ct);
        // Preserve the small SQL projection: no blobs or full ingredient/step collections.
        var items = await query.OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.Id)
            .Skip((number - 1) * size).Take(size)
            .Select(x => new RecipeCard(x.Id, x.Title, x.Description, x.Servings, x.UpdatedAtUtc,
                x.Image != null ? "/api/recipes/" + x.Id + "/image" : null)).ToListAsync(ct);
        return new(new(items, total, number, size), null);
    }
}
