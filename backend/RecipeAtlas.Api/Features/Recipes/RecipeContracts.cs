namespace RecipeAtlas.Api;

public sealed record IngredientInput(string? Name, string? Quantity, string? Unit, string? Note);
public sealed record RecipeInput(
    string? Title, string? Description, string? SourceUrl, int Servings,
    List<IngredientInput?>? Ingredients, List<string?>? Steps);
public sealed record IngredientResponse(string Name, string Quantity, string Unit, string? Note);
public sealed record RecipeResponse(
    Guid Id, string Title, string? Description, string? SourceUrl, int Servings,
    IReadOnlyList<IngredientResponse> Ingredients, IReadOnlyList<string> Steps,
    string? ImageUrl, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public static class RecipeMapping
{
    public static RecipeResponse ToResponse(Recipe recipe, bool hasImage) => new(
        recipe.Id, recipe.Title, recipe.Description, recipe.SourceUrl, recipe.Servings,
        recipe.Ingredients.OrderBy(x => x.Position)
            .Select(x => new IngredientResponse(x.Name, x.Quantity, x.Unit, x.Note)).ToArray(),
        recipe.Steps.OrderBy(x => x.Position).Select(x => x.Instruction).ToArray(),
        hasImage ? $"/api/recipes/{recipe.Id}/image" : null,
        recipe.CreatedAtUtc, recipe.UpdatedAtUtc);

    public static void Apply(Recipe recipe, RecipeInput input)
    {
        recipe.Title = input.Title!.Trim();
        recipe.Description = Clean(input.Description);
        recipe.SourceUrl = Clean(input.SourceUrl);
        recipe.Servings = input.Servings;
        recipe.UpdatedAtUtc = DateTime.UtcNow;
        recipe.Ingredients.Clear();
        recipe.Ingredients.AddRange(input.Ingredients!.Select((x, index) => new Ingredient
        {
            Name = x!.Name!.Trim(), Quantity = x.Unit == "toTaste" ? "" : QuantityText.Parse(x.Quantity!)!.Canonical, Unit = x.Unit!,
            Note = Clean(x.Note), Position = index
        }));
        recipe.Steps.Clear();
        recipe.Steps.AddRange(input.Steps!.Select((x, index) => new RecipeStep
        {
            Instruction = x!.Trim(), Position = index
        }));
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class RecipeValidation
{
    public static Dictionary<string, string[]> Check(RecipeInput input)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string field, string message) => errors[field] = [message];
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 200)
            Add("title", "Enter a title with 1 to 200 characters.");
        if (input.Description?.Length > 4000)
            Add("description", "Use at most 4000 characters.");
        if (!string.IsNullOrWhiteSpace(input.SourceUrl) &&
            (input.SourceUrl.Length > 2048 ||
             !Uri.TryCreate(input.SourceUrl.Trim(), UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
             string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)))
            Add("sourceUrl", "Enter an http or https URL without embedded credentials.");
        if (input.Servings is < 1 or > 1000)
            Add("servings", "Servings must be between 1 and 1000.");
        if (input.Ingredients is null || input.Ingredients.Count is < 1 or > 100)
            Add("ingredients", "Include 1 to 100 ingredients.");
        else
            for (var i = 0; i < input.Ingredients.Count; i++)
            {
                var item = input.Ingredients[i];
                var field = $"ingredients[{i}]";
                if (item is null) { Add(field, "Ingredient cannot be null."); continue; }
                if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 200)
                    Add(field + ".name", "Use 1 to 200 characters.");
                if (item.Unit is null || !Units.Codes.Contains(item.Unit))
                    Add(field + ".unit", "Choose a unit from /api/units.");
                if (item.Unit == "toTaste")
                {
                    if (item.Quantity != "")
                        Add(field + ".quantity", "Use an empty string for a toTaste quantity.");
                }
                else if (QuantityText.Parse(item.Quantity) is null)
                    Add(field + ".quantity", "Use a quantity or ascending range between 0.001 and 100000 (e.g. 2, 1/2, 3-4), up to 64 characters.");
                if (item.Note?.Length > 300)
                    Add(field + ".note", "Use at most 300 characters.");
            }
        if (input.Steps is null || input.Steps.Count is < 1 or > 100)
            Add("steps", "Include 1 to 100 steps.");
        else
            for (var i = 0; i < input.Steps.Count; i++)
                if (string.IsNullOrWhiteSpace(input.Steps[i]) || input.Steps[i]!.Length > 4000)
                    Add($"steps[{i}]", "Use 1 to 4000 characters.");
        return errors;
    }
}
