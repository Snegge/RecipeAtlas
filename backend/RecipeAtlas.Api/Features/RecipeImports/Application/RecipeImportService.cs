namespace Recipoke.Api.Features.RecipeImports.Application;

public sealed class RecipeImportService
{
    private readonly RecipeImporterRegistration[] _importers;
    private readonly RecipeTextExtractor _textExtractor;

    public RecipeImportService(IEnumerable<RecipeImporterRegistration> importers, RecipeTextExtractor textExtractor)
    {
        _importers = importers
            .OrderByDescending(x => x.Provider.Priority)
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

        var importer = _importers.FirstOrDefault(x => x.Provider.CanHandle(uri));

        if (importer is null)
        {
            throw new RecipeImportException(
                "This recipe source is not supported.",
                400);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        timeout.CancelAfter(importer.Policy.Deadline);

        try
        {
            return await importer.Provider.ImportAsync(uri, timeout.Token);
        }
        catch (Exception error) when (importer.Policy.RetrievalFailure is not null && error is HttpRequestException or System.Net.Sockets.SocketException or IOException)
        {
            throw new RecipeImportException(importer.Policy.RetrievalFailure!.Value.Message,
                502, importer.Policy.RetrievalFailure!.Value.Code, importer.Policy.CanPasteText);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new RecipeImportException(
                importer.Policy.TimeoutMessage,
                504, "import_timeout", importer.Policy.CanPasteText);
        }
    }
}
