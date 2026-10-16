using Recipoke.Api.Features.Recipes.Application;

namespace Recipoke.Api.Features.Recipes.Api;

public static class RecipeEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/units", () => Results.Ok(Units.All));
        var recipes = api.MapGroup("/recipes");
        recipes.MapGet("/", async (string? search, int? page, int? pageSize, ListRecipes operation, CancellationToken ct) =>
        {
            var result = await operation.ExecuteAsync(search, page, pageSize, ct);
            return result.Error is { } error ? Results.Problem(statusCode: 400, title: error) : Results.Ok(result.Page);
        });
        recipes.MapGet("/{id:guid}", async (Guid id, GetRecipe operation, CancellationToken ct) =>
            await operation.ExecuteAsync(id, ct) is { } recipe ? Results.Ok(recipe) : Results.NotFound());
        recipes.MapPost("/", async (RecipeInput input, CreateRecipe operation, CancellationToken ct) =>
        {
            var result = await operation.ExecuteAsync(input, ct);
            return result.Errors.Count > 0 ? Results.ValidationProblem(result.Errors) :
                Results.Created($"/api/recipes/{result.Recipe!.Id}", result.Recipe);
        });
        recipes.MapPut("/{id:guid}", async (Guid id, RecipeInput input, UpdateRecipe operation, CancellationToken ct) =>
        {
            var result = await operation.ExecuteAsync(id, input, ct);
            if (result.Errors.Count > 0) return Results.ValidationProblem(result.Errors);
            return result.Recipe is { } recipe ? Results.Ok(recipe) : Results.NotFound();
        });
        recipes.MapDelete("/{id:guid}", async (Guid id, DeleteRecipe operation, CancellationToken ct) =>
            await operation.ExecuteAsync(id, ct) ? Results.NoContent() : Results.NotFound());
        recipes.MapGet("/{id:guid}/image", async (Guid id, GetRecipeImage operation, CancellationToken ct) =>
            await operation.ExecuteAsync(id, ct) is { } image ? Results.File(image.Bytes, image.ContentType) : Results.NotFound());
        recipes.MapPut("/{id:guid}/image", async (Guid id, HttpRequest request, ReplaceRecipeImage operation, CancellationToken ct) =>
        {
            var error = await operation.ExecuteAsync(id, request.Body, request.ContentLength, ct);
            return error switch
            {
                ImageReplacementError.TooLarge => Results.Problem(statusCode: 413, title: "Image must be 5 MiB or smaller."),
                ImageReplacementError.UnsupportedType => Results.Problem(statusCode: 415, title: "Upload a JPEG, PNG, or WebP image."),
                ImageReplacementError.NotFound => Results.NotFound(),
                _ => Results.Ok(new { imageUrl = $"/api/recipes/{id}/image" })
            };
        });
        recipes.MapDelete("/{id:guid}/image", async (Guid id, RemoveRecipeImage operation, CancellationToken ct) =>
            await operation.ExecuteAsync(id, ct) ? Results.NoContent() : Results.NotFound());
    }
}
