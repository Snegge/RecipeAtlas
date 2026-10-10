namespace RecipeAtlas.Api.Features.RecipeImport;

public sealed record RecipeImportRequest(string? Url);

public sealed record ImportedIngredient(
    string Name,
    string Quantity,
    string? Unit,
    string? Note,
    string OriginalText,
    bool RequiresReview,
    string? ReviewReason = null);

public sealed record RecipeImportDraft(
    string Title,
    string? Description,
    string SourceUrl,
    int? Servings,
    string? OriginalYield,
    IReadOnlyList<ImportedIngredient> Ingredients,
    IReadOnlyList<string> Steps,
    string? ExternalImageUrl);

public sealed record RecipeImportResult(
    string SourceType,
    RecipeImportDraft Draft,
    IReadOnlyList<string> Warnings);

public sealed class RecipeImportException : Exception
{
    public int StatusCode { get; }

    public RecipeImportException(string message, int statusCode = 422)
        : base(message)
    {
        StatusCode = statusCode;
    }
}