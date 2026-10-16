namespace Recipoke.Api.Features.RecipeImports.Application;

/// <summary>
/// Selection is side-effect-free. ImportAsync is called only after CanHandle succeeds.
/// Propagate caller cancellation; throw RecipeImportException for supported, actionable
/// failures. Never persist a recipe. Partial drafts retain missing fields/review flags;
/// retrieval failure or unusable output must never become a successful empty draft.
/// Execution deadlines/error translation belong to the registration descriptor.
/// </summary>
public interface IRecipeImporter
{
    int Priority { get; }

    bool CanHandle(Uri url);

    Task<RecipeImportResult> ImportAsync(
        Uri url,
        CancellationToken cancellationToken = default);
}