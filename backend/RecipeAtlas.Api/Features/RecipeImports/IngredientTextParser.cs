namespace RecipeAtlas.Api.Features.RecipeImport;

// Website extraction and normal save validation use the same quantity grammar.
public static class IngredientTextParser
{
    public static ImportedIngredient Parse(string originalText)
    {
        var draft = IngredientParsing.ParseDraft(originalText);
        return new(draft.Name, draft.Quantity, string.IsNullOrEmpty(draft.Unit) ? null : draft.Unit,
            draft.Note, draft.OriginalText, draft.RequiresReview, draft.ReviewReason);
    }
}
