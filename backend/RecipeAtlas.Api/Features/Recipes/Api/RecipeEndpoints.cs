using Recipoke.Api.Features.Recipes.Application;

namespace Recipoke.Api.Features.Recipes.Api;

public static class RecipeEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/units", () => Results.Ok(Units.All)).Produces<UnitDefinition[]>();
        // Retired unscoped content routes never grant access to legacy or private data.
        api.MapMethods("/recipes/{**path}", ["GET", "POST", "PUT", "DELETE"], () => Results.NotFound()).ExcludeFromDescription();
        var recipes = api.MapGroup("/cookbooks/{cookbookId:guid}/recipes");
        recipes.MapGet("/", async (Guid cookbookId, string? search, int? page, int? pageSize, ListRecipes operation, CancellationToken ct) =>
        {
            var result = await operation.ExecuteAsync(cookbookId, search, page, pageSize, ct);
            return result.Error is { } error ? Results.Problem(statusCode: 400, title: error) : Results.Ok(result.Page);
        }).Produces<RecipePage>().ProducesProblem(400);
        recipes.MapGet("/{id:guid}", async (Guid cookbookId, Guid id, GetRecipe operation, CancellationToken ct) =>
            await operation.ExecuteAsync(cookbookId, id, ct) is { } recipe ? Results.Ok(recipe) : Results.NotFound()).Produces<RecipeResponse>().Produces(404);
        recipes.MapPost("/", async (Guid cookbookId, RecipeInput input, CreateRecipe operation, CancellationToken ct) =>
        {
            var result = await operation.ExecuteAsync(cookbookId, input, ct);
            return result.Errors.Count > 0 ? Results.ValidationProblem(result.Errors) :
                Results.Created($"/api/cookbooks/{cookbookId}/recipes/{result.Recipe!.Id}", result.Recipe);
        }).Produces<RecipeResponse>(201).ProducesValidationProblem();
        recipes.MapPut("/{id:guid}", async (Guid cookbookId, Guid id, RecipeInput input, UpdateRecipe operation, CancellationToken ct) =>
        {
            var result = await operation.ExecuteAsync(cookbookId, id, input, ct);
            if (result.Errors.Count > 0) return Results.ValidationProblem(result.Errors);
            return result.Recipe is { } recipe ? Results.Ok(recipe) : Results.NotFound();
        }).Produces<RecipeResponse>().ProducesValidationProblem().Produces(404);
        recipes.MapDelete("/{id:guid}", async (Guid cookbookId, Guid id, HttpRequest request, DeleteRecipe operation, CancellationToken ct) =>
            await operation.ExecuteAsync(cookbookId, id, new VersionPrecondition(request.Headers.IfMatch.ToString(), true), ct) ? Results.NoContent() : Results.NotFound()).Produces(204).Produces(404);
        recipes.MapGet("/{id:guid}/image", async (Guid cookbookId, Guid id, GetRecipeImage operation, CancellationToken ct) =>
            await operation.ExecuteAsync(cookbookId, id, ct) is { } image ? Results.File(image.Bytes, image.ContentType) : Results.NotFound()).Produces(200, contentType: "image/png", additionalContentTypes: ["image/jpeg", "image/webp"]).Produces(404);
        recipes.MapPut("/{id:guid}/image", async (Guid cookbookId, Guid id, HttpRequest request, ReplaceRecipeImage operation, CancellationToken ct) =>
        {
            var result = await operation.ExecuteAsync(cookbookId, id, request.Body, request.ContentLength, new VersionPrecondition(request.Headers.IfMatch.ToString(), true), ct);
            return result.Error switch
            {
                ImageReplacementError.TooLarge => Results.Problem(statusCode: 413, title: "Image must be 5 MiB or smaller."),
                ImageReplacementError.UnsupportedType => Results.Problem(statusCode: 415, title: "Upload a JPEG, PNG, or WebP image."),
                ImageReplacementError.NotFound => Results.NotFound(),
                _ => Results.Ok(result.Image)
            };
        }).Produces<ImageResponse>().ProducesProblem(413).ProducesProblem(415).Produces(404);
        recipes.MapDelete("/{id:guid}/image", async (Guid cookbookId, Guid id, HttpRequest request, RemoveRecipeImage operation, CancellationToken ct) =>
            Results.Ok(await operation.ExecuteAsync(cookbookId, id, new VersionPrecondition(request.Headers.IfMatch.ToString(), true), ct))).Produces<ImageResponse>().Produces(404);
    }
}
