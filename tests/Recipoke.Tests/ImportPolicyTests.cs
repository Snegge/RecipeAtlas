using Recipoke.Api.Features.RecipeImports.Application;
using Recipoke.Api.Features.RecipeImports.Contracts;

internal static class ImportPolicyTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var extractor = new RecipeTextExtractor(new NeverModel());
        var preferred = new SubstituteProvider(100);
        var fallback = new SubstituteProvider(0);
        var service = new RecipeImportService([new(fallback, ImportExecutionPolicy.Website), new(preferred, ImportExecutionPolicy.Social)], extractor);
        await service.ImportAsync("https://example.com/recipe");
        check(preferred.Calls == 1 && fallback.Calls == 0, "Provider priority applies to substitutes");
        check(ImportExecutionPolicy.Website.Deadline.TotalSeconds == 20 && ImportExecutionPolicy.Social.Deadline.TotalSeconds == 80, "Existing registration deadlines");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        preferred.WaitForCancellation = true;
        try { await service.ImportAsync("https://example.com/recipe", cancelled.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { check(true, "Caller cancellation remains cancellation"); }
        var shortPolicy = new ImportExecutionPolicy(TimeSpan.FromMilliseconds(20), "Policy deadline", true);
        service = new RecipeImportService([new(preferred, shortPolicy)], extractor);
        try { await service.ImportAsync("https://example.com/recipe"); throw new Exception("Deadline ignored"); }
        catch (RecipeImportException e) { check(e.StatusCode == 504 && e.Code == "import_timeout" && e.CanPasteText && e.Message == "Policy deadline", "Policy timeout independent of concrete type"); }
        preferred.WaitForCancellation = false; preferred.Failure = new HttpRequestException();
        service = new RecipeImportService([new(preferred, ImportExecutionPolicy.Social)], extractor);
        try { await service.ImportAsync("https://example.com/recipe"); throw new Exception("Failure swallowed"); }
        catch (RecipeImportException e) { check(e.Code == "social_retrieval_failed" && e.CanPasteText, "Configured retrieval error for substitute provider"); }
        service = new RecipeImportService([new(preferred, ImportExecutionPolicy.Website)], extractor);
        try { await service.ImportAsync("https://example.com/recipe"); throw new Exception("Failure swallowed"); }
        catch (HttpRequestException) { check(true, "Website retrieval failure preserved for HTTP adapter"); }
    }
    private sealed class NeverModel : IRecipeTextModel
    {
        public Task<string> ExtractAsync(string sourceText, CancellationToken token) => throw new Exception("Model must not run for provider test");
    }
    private sealed class SubstituteProvider(int priority) : IRecipeImporter
    {
        public int Priority => priority;
        public int Calls { get; private set; }
        public bool WaitForCancellation { get; set; }
        public Exception? Failure { get; set; }
        public bool CanHandle(Uri url) => true;
        public async Task<RecipeImportResult> ImportAsync(Uri url, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (Failure is not null) throw Failure;
            return new("fixture", new("Partial", null, url.AbsoluteUri, null, null, [], [], null), []);
        }
    }
}
