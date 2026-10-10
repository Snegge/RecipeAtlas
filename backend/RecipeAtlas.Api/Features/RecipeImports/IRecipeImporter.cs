namespace RecipeAtlas.Api.Features.RecipeImport;

public interface IRecipeImporter
{
    int Priority { get; }

    bool CanHandle(Uri url);

    Task<RecipeImportResult> ImportAsync(
        Uri url,
        CancellationToken cancellationToken = default);
}