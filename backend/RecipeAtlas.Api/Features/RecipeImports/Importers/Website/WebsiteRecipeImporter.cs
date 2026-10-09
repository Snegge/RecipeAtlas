namespace RecipeAtlas.Api.Features.RecipeImport;

public sealed class WebsiteRecipeImporter : IRecipeImporter
{
    private readonly SafeWebsiteClient _websiteClient;
    private readonly JsonLdRecipeExtractor _extractor;

    public int Priority => 0;

    public WebsiteRecipeImporter(
        SafeWebsiteClient websiteClient,
        JsonLdRecipeExtractor extractor)
    {
        _websiteClient = websiteClient;
        _extractor = extractor;
    }

    public bool CanHandle(Uri url)
    {
        return url.Scheme == Uri.UriSchemeHttps;
    }

    public async Task<RecipeImportResult> ImportAsync(
        Uri url,
        CancellationToken cancellationToken = default)
    {
        var page = await _websiteClient.DownloadAsync(
            url,
            cancellationToken);

        return _extractor.Extract(
            page.Html,
            page.FinalUrl,
            cancellationToken);
    }
}