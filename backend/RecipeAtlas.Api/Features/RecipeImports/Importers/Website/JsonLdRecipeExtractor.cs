using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace RecipeAtlas.Api.Features.RecipeImport;

public sealed class JsonLdRecipeExtractor
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    public RecipeImportResult Extract(
        string html,
        Uri sourceUrl,
        CancellationToken cancellationToken = default)
    {
        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);

        var recipes = new List<JsonElement>();
        var malformedBlocks = 0;

        foreach (var script in document.QuerySelectorAll("script[type]"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var type = script.GetAttribute("type")?
                .Split(';')[0]
                .Trim();

            if (!string.Equals(
                    type,
                    "application/ld+json",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var json = JsonDocument.Parse(
                    script.TextContent.Trim().TrimStart('\uFEFF'),
                    new JsonDocumentOptions
                    {
                        MaxDepth = 64,
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip
                    });

                CollectRecipes(json.RootElement, recipes, cancellationToken);
            }
            catch (JsonException)
            {
                // A malformed block should not hide another valid recipe.
                malformedBlocks++;
            }
        }

        if (recipes.Count == 0)
        {
            throw new RecipeImportException(
                "No structured recipe was found. This version imports " +
                "websites containing Schema.org Recipe JSON-LD.");
        }

        var recipe = recipes
            .OrderByDescending(Score)
            .First();

        var warnings = new List<string>();

        if (recipes.Count > 1)
        {
            warnings.Add(
                "Several recipes were found. The most complete one was selected.");
        }

        if (malformedBlocks > 0)
            warnings.Add("Some invalid structured-data blocks were skipped.");

        var title = Clean(Scalar(Get(recipe, "name")));
        var description = Clean(Scalar(Get(recipe, "description")));
        
        var ingredients = ReadIngredients(Get(recipe, "recipeIngredient"))
            .Select(Clean)
            .Where(x => x.Length > 0)
            .Select(IngredientTextParser.Parse)
            .ToArray();

        var steps = ReadSteps(Get(recipe, "recipeInstructions"))
            .Select(Clean)
            .Where(x => x.Length > 0)
            .ToArray();

        var yieldValues = ReadYieldValues(Get(recipe, "recipeYield"))
            .Select(Clean)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var originalYield = string.Join(" / ", yieldValues);

        var servingCandidates = yieldValues
            .Select(ParseServings)
            .Where(x => x.HasValue)
            .Select(x => x.GetValueOrDefault())
            .Distinct()
            .ToArray();

        // Keep conflicting serving counts unresolved.
        int? servings = servingCandidates.Length == 1
            ? servingCandidates[0]
            : null;

        if (servingCandidates.Length > 1)
        {
            warnings.Add(
                "The website supplied conflicting serving counts. Choose one.");
        }

        if (title.Length == 0)
            warnings.Add("The recipe title is missing.");

        if (ingredients.Length == 0)
            warnings.Add("No ingredients were found.");

        if (steps.Length == 0)
            warnings.Add("No preparation steps were found.");

        if (servings is null)
        {
            warnings.Add(
                "Confirm the serving count. The original yield is included.");
        }

        var ingredientsNeedingReview = ingredients.Count(x => x.RequiresReview);

        if (ingredientsNeedingReview > 0)
        {
            warnings.Add(
                $"{ingredientsNeedingReview} ingredient(s) need review. " +
                "Check quantities, units, and the preserved original text.");
        }

        if (title.Length > 200 ||
            description.Length > 4000 ||
            ingredients.Length > 100 ||
            steps.Length > 100 ||
            steps.Any(x => x.Length > 4000))
        {
            warnings.Add(
                "Some imported fields exceed RecipeAtlas save limits. " +
                "Shorten them in the editor.");
        }

        var imageUrl = ReadImageUrl(Get(recipe, "image"), sourceUrl);

        if (imageUrl is null)
        {
            string[] selectors =
            [
                "meta[property='og:image:secure_url']",
                "meta[property='og:image']",
                "meta[property='og:image:url']",
                "meta[name='twitter:image']",
                "meta[property='twitter:image']",
                "meta[name='twitter:image:src']"
            ];

            foreach (var selector in selectors)
            {
                foreach (var meta in document.QuerySelectorAll(selector))
                {
                    imageUrl = ResolveImageUrl(
                        meta.GetAttribute("content"),
                        sourceUrl);

                    if (imageUrl is not null)
                        break;
                }

                if (imageUrl is not null)
                    break;
            }
        }

        if (imageUrl is null)
        {
            warnings.Add(
                "No usable image URL was found. You can upload a photo in the editor.");
        }

        var draft = new RecipeImportDraft(
            Title: title,
            Description: NullIfEmpty(description),
            SourceUrl: sourceUrl.AbsoluteUri,
            Servings: servings,
            OriginalYield: NullIfEmpty(originalYield),
            Ingredients: ingredients,
            Steps: steps,
            ExternalImageUrl: imageUrl);

        return new RecipeImportResult("website", draft, warnings);
    }

    private static void CollectRecipes(
        JsonElement element,
        List<JsonElement> results,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                CollectRecipes(child, results, cancellationToken);

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (IsRecipeType(Get(element, "@type")))
            results.Add(element.Clone());

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is
                JsonValueKind.Object or JsonValueKind.Array)
            {
                CollectRecipes(property.Value, results, cancellationToken);
            }
        }
    }

    private static IEnumerable<string> ReadYieldValues(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                foreach (var text in ReadYieldValues(item))
                    yield return text;
            }

            yield break;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();

            if (!string.IsNullOrWhiteSpace(text))
                yield return text;
        }
        else if (value.ValueKind == JsonValueKind.Number)
        {
            yield return value.GetRawText();
        }
    }

    private static bool IsRecipeType(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Any(IsRecipeType);

        if (value.ValueKind != JsonValueKind.String)
            return false;

        var type = value.GetString();

        return type is
            "Recipe" or
            "https://schema.org/Recipe" or
            "http://schema.org/Recipe";
    }

    private static int Score(JsonElement recipe)
    {
        var score = 0;

        if (!string.IsNullOrWhiteSpace(Scalar(Get(recipe, "name"))))
            score += 1;

        if (ReadIngredients(Get(recipe, "recipeIngredient")).Any())
            score += 4;

        if (ReadSteps(Get(recipe, "recipeInstructions")).Any())
            score += 4;

        return score;
    }

    private static JsonElement Get(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value)
            ? value
            : default;
    }

    private static string? Scalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.Array => string.Join(
                " / ",
                value.EnumerateArray()
                    .Select(Scalar)
                    .Where(x => !string.IsNullOrWhiteSpace(x))),
            _ => null
        };
    }

    private static IEnumerable<string> ReadIngredients(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                foreach (var ingredient in ReadIngredients(item))
                    yield return ingredient;
            }

            yield break;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            foreach (var line in SplitText(value.GetString() ?? ""))
                yield return line;

            yield break;
        }

        // Schema.org also allows PropertyValue ingredients.
        if (value.ValueKind == JsonValueKind.Object)
        {
            var name = Scalar(Get(value, "name"));
            var amount = Scalar(Get(value, "value"));
            var unit = Scalar(Get(value, "unitText"))
                       ?? Scalar(Get(value, "unitCode"));

            var text = string.Join(
                " ",
                new[] { amount, unit, name }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));

            if (!string.IsNullOrWhiteSpace(text))
                yield return text;
        }
    }

    private static IEnumerable<string> ReadSteps(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                foreach (var step in ReadSteps(item))
                    yield return step;
            }

            yield break;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            foreach (var line in SplitText(value.GetString() ?? ""))
                yield return line;

            yield break;
        }

        if (value.ValueKind != JsonValueKind.Object)
            yield break;

        var children = Get(value, "itemListElement");

        if (children.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
        {
            foreach (var step in ReadSteps(children))
                yield return step;

            yield break;
        }

        // Supports ListItem wrappers.
        var itemValue = Get(value, "item");

        if (itemValue.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            foreach (var step in ReadSteps(itemValue))
                yield return step;

            yield break;
        }

        var text = Scalar(Get(value, "text"))
                   ?? Scalar(Get(value, "name"));

        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (var line in SplitText(text))
                yield return line;
        }
    }

    private static IEnumerable<string> SplitText(string value)
    {
        var withLineBreaks = Regex.Replace(
            value,
            @"<br\s*/?>|</(?:p|div|li)>",
            "\n",
            RegexOptions.IgnoreCase,
            RegexTimeout);

        return withLineBreaks.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        using var document = new HtmlParser().ParseDocument(value);

        foreach (var element in document.QuerySelectorAll(
                     "script, style, noscript"))
        {
            element.Remove();
        }

        var text = document.Body?.TextContent ?? "";

        return Regex.Replace(
            text,
            @"\s+",
            " ",
            RegexOptions.None,
            RegexTimeout).Trim();
    }

    private static int? ParseServings(string value)
    {
        // Do not interpret "12 cookies" or "1 loaf" as serving counts.
        var match = Regex.Match(
            value,
            @"^(?<count>\d{1,4})(?:\s*(?:servings?|portions?|portionen|personen))?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout);

        if (match.Success &&
            int.TryParse(
                match.Groups["count"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var count) &&
            count is >= 1 and <= 1000)
        {
            return count;
        }

        return null;
    }

    private static string? ReadImageUrl(
        JsonElement value,
        Uri sourceUrl)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var found = ReadImageUrl(item, sourceUrl);

                if (found is not null)
                    return found;
            }

            return null;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            var found =
                ReadImageUrl(Get(value, "contentUrl"), sourceUrl) ??
                ReadImageUrl(Get(value, "url"), sourceUrl);

            if (found is not null)
                return found;

            // Some sites use a direct image URL as @id.
            // Fragment identifiers normally reference metadata nodes instead.
            var id = Get(value, "@id");

            if (id.ValueKind == JsonValueKind.String)
            {
                var resolved = ResolveImageUrl(id.GetString(), sourceUrl);

                if (resolved is not null)
                {
                    var uri = new Uri(resolved);

                    if (string.IsNullOrEmpty(uri.Fragment) &&
                        uri.GetLeftPart(UriPartial.Path) !=
                        sourceUrl.GetLeftPart(UriPartial.Path))
                    {
                        return resolved;
                    }
                }
            }

            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? ResolveImageUrl(value.GetString(), sourceUrl)
            : null;
    }

    private static string? ResolveImageUrl(
        string? value,
        Uri sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        if (value.StartsWith('#') ||
            !Uri.TryCreate(sourceUrl, value, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsoluteUri.Length > 2048)
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    private static string? NullIfEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}