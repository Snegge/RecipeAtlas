namespace Recipoke.Api.Features.RecipeImports.Application;

public interface IRecipeTextModel
{
    Task<string> ExtractAsync(string sourceText, CancellationToken cancellationToken);
}

public interface ISocialDescriptionRetriever
{
    Task<string> RetrieveAsync(SocialSource source, CancellationToken cancellationToken);
}
public sealed record MetadataProcessResult(int ExitCode, string Output, string Error);
public interface IMetadataProcessRunner
{
    Task<MetadataProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

