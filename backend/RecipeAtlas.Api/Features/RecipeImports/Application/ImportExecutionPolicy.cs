namespace Recipoke.Api.Features.RecipeImports.Application;

// The composition root chooses policy, not the concrete provider type at runtime.
public sealed record RecipeImporterRegistration(IRecipeImporter Provider, ImportExecutionPolicy Policy);
public sealed record ImportExecutionPolicy(TimeSpan Deadline, string TimeoutMessage,
    bool CanPasteText = false, string? RetrievalFailureMessage = null)
{
    public static readonly ImportExecutionPolicy Website = new(TimeSpan.FromSeconds(20),
        "The recipe website took too long to respond.");
    public static readonly ImportExecutionPolicy Social = new(TimeSpan.FromSeconds(80),
        "Social import timed out. Try again or paste the description.", true,
        "The platform could not be reached. Open the public post and paste its description instead.");
}
