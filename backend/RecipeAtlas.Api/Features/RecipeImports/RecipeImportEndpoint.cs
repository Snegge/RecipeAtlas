namespace RecipeAtlas.Api.Features.RecipeImport;

public static class RecipeImportEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/recipes/import", ImportAsync)
            .RequireAuthorization()
            .RequireRateLimiting("recipe-import")
            .WithName("ImportRecipe")
            .WithTags("Recipe Import");
    }

    private static async Task<IResult> ImportAsync(
        RecipeImportRequest request,
        RecipeImportService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = request.Text is not null
                ? await service.ImportTextAsync(request.Text, request.Url, cancellationToken)
                : await service.ImportAsync(request.Url, cancellationToken);

            return Results.Ok(result);
        }
        catch (RecipeImportException exception)
        {
            return Results.Problem(
                statusCode: exception.StatusCode,
                title: exception.Message,
                extensions: new Dictionary<string, object?> { ["code"] = exception.Code, ["canPasteText"] = exception.CanPasteText });
        }
        catch (HttpRequestException)
        {
            return Results.Problem(
                statusCode: 502,
                title: "The recipe website could not be reached.");
        }
        catch (IOException)
        {
            return Results.Problem(
                statusCode: 502,
                title: "The connection ended while downloading the recipe.");
        }
    }
}
