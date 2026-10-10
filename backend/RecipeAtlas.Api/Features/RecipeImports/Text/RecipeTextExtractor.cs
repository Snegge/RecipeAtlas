using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RecipeAtlas.Api.Features.RecipeImport;

public interface IRecipeTextModel
{
    Task<string> ExtractAsync(string sourceText, CancellationToken cancellationToken);
}

public sealed class ExtractedRecipe
{
    [JsonRequired] public string? Title { get; init; }
    [JsonRequired] public string? Description { get; init; }
    [JsonRequired] public int? Servings { get; init; }
    [JsonRequired] public string? OriginalYield { get; init; }
    [JsonRequired] public string[] Ingredients { get; init; } = [];
    [JsonRequired] public string[] Steps { get; init; } = [];
    [JsonRequired] public string[] Flags { get; init; } = [];
    [JsonRequired] public bool UsableRecipe { get; init; }
    [JsonRequired] public bool MultipleRecipes { get; init; }
    [JsonRequired] public bool RefersElsewhere { get; init; }
}

public sealed class RecipeTextExtractor(IRecipeTextModel model)
{
    public const int MaxSourceLength = 20000;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 12
    };
    public static void ValidateSource(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxSourceLength)
            throw new RecipeImportException("Paste a written recipe or description, up to 20,000 characters.", 400, "invalid_text");
    }

    public async Task<RecipeImportResult> ExtractAsync(string text, string? sourceUrl, string sourceType,
        CancellationToken cancellationToken = default)
    {
        ValidateSource(text);
        var response = await model.ExtractAsync(text, cancellationToken);
        ExtractedRecipe? recipe;
        try
        {
            if (response.Length > 64 * 1024) throw new JsonException();
            recipe = JsonSerializer.Deserialize<ExtractedRecipe>(response, JsonOptions);
        }
        catch (JsonException) { throw InvalidResponse(); }
        if (recipe is null) throw InvalidResponse();
        var source = Normalize(text);
        bool Quote(string? value, int max) => value is null ||
            (value.Length <= max && !string.IsNullOrWhiteSpace(value) && source.Contains(Normalize(value), StringComparison.Ordinal));
        bool Lines(string[]? values, int maxCount, int maxLength) => values is not null && values.Length <= maxCount &&
            values.All(value => value is not null && Quote(value, maxLength));
        if (!Quote(recipe.Title, 200) || !Quote(recipe.Description, 4000) || !Quote(recipe.OriginalYield, 200) ||
            !Lines(recipe.Ingredients, 100, 600) || !Lines(recipe.Steps, 100, 4000) ||
            recipe.Flags is null || recipe.Flags.Length > 20 || recipe.Flags.Any(flag => string.IsNullOrWhiteSpace(flag) || flag.Length > 300) ||
            recipe.Servings is < 1 or > 1000)
            throw InvalidResponse();
        if (recipe.Servings is { } servings)
        {
            if (recipe.OriginalYield is null) throw InvalidResponse();
            var match = Regex.Match(recipe.OriginalYield,
                @"(?:\bserves\s*(\d{1,4})\b|\b(\d{1,4})\s*(?:servings?|portions?|Portionen?|people|Personen?)\b)", RegexOptions.IgnoreCase);
            var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (!match.Success || !int.TryParse(value, out var stated) || stated != servings) throw InvalidResponse();
        }
        if (recipe.MultipleRecipes)
            throw new RecipeImportException("This description contains several recipes. Paste only the text of the recipe you want to import.",
                422, "multiple_recipes", true);
        if (!recipe.UsableRecipe || (recipe.Ingredients.Length == 0 && recipe.Steps.Length == 0))
        {
            if (recipe.RefersElsewhere || Regex.IsMatch(text,
                @"(?:recipe|rezept).{0,50}(?:comments?|kommentare?n)|link.{0,15}(?:in|im|en).{0,10}bio", RegexOptions.IgnoreCase))
                throw new RecipeImportException("The recipe is in comments or a bio link. Description-only import cannot read those. Paste the recipe text yourself or import its recipe website directly.",
                    422, "recipe_elsewhere", true);
            throw new RecipeImportException("The written description contains no usable recipe. Paste recipe text with ingredients or instructions; the video title cannot supply missing information.",
                422, "no_recipe", true);
        }
        var warnings = new List<string> { "AI extraction: check the ingredients and instructions against the source before saving." };
        warnings.AddRange(recipe.Flags);
        if (recipe.RefersElsewhere) warnings.Add("Some details are in comments or a bio link. Description-only import cannot retrieve them; paste those details yourself.");
        if (recipe.Title is null) warnings.Add("No recipe title was stated. Enter a title.");
        if (recipe.Servings is null) warnings.Add("Servings were not stated. Enter the correct serving count.");
        if (recipe.Ingredients.Length == 0) warnings.Add("Ingredients are missing. Add them before saving.");
        if (recipe.Steps.Length == 0) warnings.Add("Instructions are missing. This is a partial draft; add instructions before saving.");
        var ingredients = recipe.Ingredients.Select(line =>
        {
            var parsed = IngredientTextParser.Parse(line);
            return parsed with { RequiresReview = true, ReviewReason =
                string.IsNullOrWhiteSpace(parsed.ReviewReason) ? "Check against the description." : parsed.ReviewReason + " Check against the description." };
        }).ToArray();
        return new(sourceType, new(recipe.Title ?? "", recipe.Description, sourceUrl ?? "", recipe.Servings,
            recipe.OriginalYield, ingredients, recipe.Steps, null), warnings);
    }
    private static string Normalize(string text) => Regex.Replace(text.Trim(), @"\s+", " ");
    public static RecipeImportException InvalidResponse() => new(
        "The AI response was invalid or included wording/values absent from the description. Try again with just the recipe text, or enter it manually.",
        502, "invalid_model_response", true);
}
