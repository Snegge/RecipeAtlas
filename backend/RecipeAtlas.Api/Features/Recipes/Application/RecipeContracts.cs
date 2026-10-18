namespace Recipoke.Api.Features.Recipes.Application;

public sealed record IngredientInput(string? Name, string? Quantity, string? Unit, string? Note);
public sealed record RecipeInput(
    string? Title, string? Description, string? SourceUrl, int Servings,
    List<IngredientInput?>? Ingredients, List<string?>? Steps, string? ExpectedVersion = null);
public sealed record IngredientResponse(string Name, string Quantity, string Unit, string? Note);
public sealed record RecipeResponse(
    Guid Id, string Title, string? Description, string? SourceUrl, int Servings,
    IReadOnlyList<IngredientResponse> Ingredients, IReadOnlyList<string> Steps,
    string? ImageUrl, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid Version);


public sealed record RecipeCard(Guid Id, string Title, string? Description, int Servings,
    DateTime UpdatedAtUtc, string? ImageUrl);
public sealed record RecipePage(IReadOnlyList<RecipeCard> Items, int Total, int Page, int PageSize);
public sealed record SaveRecipeResult(RecipeResponse? Recipe, Dictionary<string, string[]> Errors);
public sealed record ListRecipesResult(RecipePage? Page, string? Error);
public enum ImageReplacementError { NotFound, TooLarge, UnsupportedType }

public sealed record ImageResponse(string? ImageUrl, Guid Version, DateTime UpdatedAtUtc);
public sealed record ImageReplacementResult(ImageReplacementError? Error, ImageResponse? Image);
