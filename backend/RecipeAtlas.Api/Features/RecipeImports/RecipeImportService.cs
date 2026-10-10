namespace RecipeAtlas.Api.Features.RecipeImport;

public sealed class RecipeImportService
{
    private readonly IRecipeImporter[] _importers;
    private readonly RecipeTextExtractor _textExtractor;

    public RecipeImportService(IEnumerable<IRecipeImporter> importers, RecipeTextExtractor textExtractor)
    {
        _importers = importers
            .OrderByDescending(x => x.Priority)
            .ToArray();
        _textExtractor = textExtractor;
    }

    public async Task<RecipeImportResult> ImportTextAsync(string? text, string? url, CancellationToken cancellationToken = default)
    {
        RecipeTextExtractor.ValidateSource(text);
        if (!string.IsNullOrWhiteSpace(url))
        {
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var source))
                throw new RecipeImportException("Enter a valid social source URL or leave it empty.", 400, "invalid_source");
            SocialPlatformUrls.ValidateHttps(source);
            // Attribution only: never fetch this URL or links embedded in the text.
            if (!source.IdnHost.Contains('.') || source.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 ||
                source.IdnHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
                throw new RecipeImportException("Use a public HTTPS source URL or leave it empty.", 400, "invalid_source");
            if (SocialPlatformUrls.IsSocialHost(source)) SocialPlatformUrls.Parse(source);
            url = source.AbsoluteUri;
        }
        else url = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(50));
        try { return await _textExtractor.ExtractAsync(text!, url, "text", timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new RecipeImportException("Text extraction timed out. Try a shorter recipe excerpt.", 504, "ai_timeout"); }
    }

    public async Task<RecipeImportResult> ImportAsync(
        string? url,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            url.Length > 2048 ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            throw new RecipeImportException(
                "Enter a valid recipe URL with at most 2048 characters.",
                400);
        }

        if (uri.Scheme != Uri.UriSchemeHttps ||
            uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new RecipeImportException(
                "Use an HTTPS URL on port 443 without embedded credentials.",
                400);
        }

        var importer = _importers.FirstOrDefault(x => x.CanHandle(uri));

        if (importer is null)
        {
            throw new RecipeImportException(
                "This recipe source is not supported.",
                400);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        timeout.CancelAfter(TimeSpan.FromSeconds(importer is SocialRecipeImporter ? 80 : 20));

        try
        {
            return await importer.ImportAsync(uri, timeout.Token);
        }
        catch (Exception error) when (importer is SocialRecipeImporter && error is HttpRequestException or System.Net.Sockets.SocketException or IOException)
        {
            throw new RecipeImportException("The platform could not be reached. Open the public post and paste its description instead.",
                502, "social_retrieval_failed", true);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new RecipeImportException(
                importer is SocialRecipeImporter ? "Social import timed out. Try again or paste the description." : "The recipe website took too long to respond.",
                504, "import_timeout", importer is SocialRecipeImporter);
        }
    }
}
