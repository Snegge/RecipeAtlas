namespace RecipeAtlas.Api.Features.RecipeImport;

public sealed class RecipeImportService
{
    private readonly IRecipeImporter[] _importers;

    public RecipeImportService(IEnumerable<IRecipeImporter> importers)
    {
        _importers = importers
            .OrderByDescending(x => x.Priority)
            .ToArray();
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

        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        try
        {
            return await importer.ImportAsync(uri, timeout.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new RecipeImportException(
                "The recipe website took too long to respond.",
                504);
        }
    }
}