using Microsoft.EntityFrameworkCore;

namespace RecipeAtlas.Api;

public static class RecipeEndpoints
{
    private const int MaxImageBytes = 5 * 1024 * 1024;

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/units", () => Results.Ok(Units.All));
        var recipes = api.MapGroup("/recipes");

        recipes.MapGet("/", async (string? search, int? page, int? pageSize,
            RecipeDb db, CancellationToken ct) =>
        {
            var number = page ?? 1;
            var size = pageSize ?? 20;
            if (number is < 1 or > 100000 || size is < 1 or > 100 || search?.Length > 200)
                return Results.Problem(statusCode: 400, title: "Use page 1..100000, pageSize 1..100, and search up to 200 characters.");
            var query = db.Recipes.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                // LIKE is ASCII case-insensitive with SQLite's default configuration.
                // Escape wildcards so searching '%' means a literal percent character.
                var text = search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                var pattern = "%" + text + "%";
                query = query.Where(x => EF.Functions.Like(x.Title, pattern, "\\") ||
                    x.Ingredients.Any(i => EF.Functions.Like(i.Name, pattern, "\\")));
            }
            var total = await query.CountAsync(ct);
            // Project small cards: no image bytes, steps, or ingredients on list requests.
            var items = await query.OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.Id)
                .Skip((number - 1) * size).Take(size)
                .Select(x => new
                {
                    x.Id, x.Title, x.Description, x.Servings, x.UpdatedAtUtc,
                    ImageUrl = x.Image != null ? "/api/recipes/" + x.Id + "/image" : null
                }).ToListAsync(ct);
            return Results.Ok(new { items, total, page = number, pageSize = size });
        });

        recipes.MapGet("/{id:guid}", async (Guid id, RecipeDb db, CancellationToken ct) =>
        {
            var recipe = await Details(db).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (recipe is null) return Results.NotFound();
            var hasImage = await db.Images.AnyAsync(x => x.RecipeId == id, ct);
            return Results.Ok(RecipeMapping.ToResponse(recipe, hasImage));
        });

        recipes.MapPost("/", async (RecipeInput input, RecipeDb db, CancellationToken ct) =>
        {
            var errors = RecipeValidation.Check(input);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var recipe = new Recipe();
            RecipeMapping.Apply(recipe, input);
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/recipes/{recipe.Id}", RecipeMapping.ToResponse(recipe, false));
        });

        recipes.MapPut("/{id:guid}", async (Guid id, RecipeInput input, RecipeDb db, CancellationToken ct) =>
        {
            var errors = RecipeValidation.Check(input);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var recipe = await Details(db).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (recipe is null) return Results.NotFound();
            RecipeMapping.Apply(recipe, input);
            await db.SaveChangesAsync(ct);
            var hasImage = await db.Images.AnyAsync(x => x.RecipeId == id, ct);
            return Results.Ok(RecipeMapping.ToResponse(recipe, hasImage));
        });

        recipes.MapDelete("/{id:guid}", async (Guid id, RecipeDb db, CancellationToken ct) =>
        {
            // SQLite foreign keys delete ingredients, steps, and the image atomically.
            var deleted = await db.Recipes.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });

        recipes.MapGet("/{id:guid}/image", async (Guid id, RecipeDb db, CancellationToken ct) =>
        {
            var image = await db.Images.AsNoTracking().SingleOrDefaultAsync(x => x.RecipeId == id, ct);
            return image is null ? Results.NotFound() : Results.File(image.Bytes, image.ContentType);
        });

        // Raw binary File/Blob body, not multipart. The same CSRF header is required here.
        recipes.MapPut("/{id:guid}/image", UploadImage);
        recipes.MapDelete("/{id:guid}/image", async (Guid id, RecipeDb db, CancellationToken ct) =>
        {
            var recipe = await db.Recipes.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (recipe is null) return Results.NotFound();
            var image = await db.Images.SingleOrDefaultAsync(x => x.RecipeId == id, ct);
            if (image is not null)
            {
                db.Images.Remove(image);
                recipe.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            return Results.NoContent();
        });
    }

    private static IQueryable<Recipe> Details(RecipeDb db) =>
        db.Recipes.Include(x => x.Ingredients).Include(x => x.Steps).AsSplitQuery();

    private static async Task<IResult> UploadImage(Guid id, HttpRequest request,
        RecipeDb db, CancellationToken ct)
    {
        if (request.ContentLength > MaxImageBytes)
            return Results.Problem(statusCode: 413, title: "Image must be 5 MiB or smaller.");
        var recipe = await db.Recipes.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (recipe is null) return Results.NotFound();
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await request.Body.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            if (output.Length + count > MaxImageBytes)
                return Results.Problem(statusCode: 413, title: "Image must be 5 MiB or smaller.");
            output.Write(buffer, 0, count);
        }
        var bytes = output.ToArray();
        var type = DetectImageType(bytes);
        if (type is null)
            return Results.Problem(statusCode: 415, title: "Upload a JPEG, PNG, or WebP image.");
        var image = await db.Images.SingleOrDefaultAsync(x => x.RecipeId == id, ct);
        if (image is null)
        {
            image = new RecipeImage { RecipeId = id };
            db.Images.Add(image);
        }
        image.Bytes = bytes;
        image.ContentType = type;
        recipe.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { imageUrl = $"/api/recipes/{id}/image" });
    }

    // Signature screening, not a full image decoder. Content-Type and filenames are untrusted.
    // Only the authenticated owner can upload or retrieve these images.
    private static string? DetectImageType(byte[] bytes)
    {
        var data = bytes.AsSpan();
        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return "image/png";
        if (data.Length >= 3 && data[0] == 255 && data[1] == 216 && data[2] == 255)
            return "image/jpeg";
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }
}
