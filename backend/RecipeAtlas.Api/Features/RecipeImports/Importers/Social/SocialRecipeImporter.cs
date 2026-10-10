namespace RecipeAtlas.Api.Features.RecipeImport;

public sealed class SocialRecipeImporter(SocialUrlResolver resolver, ISocialDescriptionRetriever retriever, RecipeTextExtractor extractor) : IRecipeImporter
{
    public int Priority => 100;
    public bool CanHandle(Uri url) => SocialPlatformUrls.IsSocialHost(url);
    public async Task<RecipeImportResult> ImportAsync(Uri url, CancellationToken cancellationToken = default)
    {
        var source = await resolver.ResolveAsync(url, cancellationToken);
        var description = await retriever.RetrieveAsync(source, cancellationToken);
        return await extractor.ExtractAsync(description, url.AbsoluteUri, source.Platform, cancellationToken);
    }
}
