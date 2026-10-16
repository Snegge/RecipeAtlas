using Recipoke.Api.Features.RecipeImports.Infrastructure.Website;
using Recipoke.Api.Features.RecipeImports.Infrastructure.Social;
using Recipoke.Api.Features.RecipeImports.Infrastructure.TextExtraction;
using System.Net.Http.Headers;

namespace Recipoke.Api.Features.RecipeImports;

public static class RecipeImportRegistration
{
    public static IServiceCollection AddRecipeImport(
        this IServiceCollection services)
    {
        services.AddHttpClient<SafeWebsiteClient>(client =>
        {
            // RecipeImportService owns the timeout for the whole operation.
            client.Timeout = Timeout.InfiniteTimeSpan;

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Recipoke/1.0");

            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("text/html"));
        })
        .ConfigurePrimaryHttpMessageHandler(
            SafeWebsiteClient.CreateHandler);

        services.AddOptions<SocialImportOptions>().BindConfiguration("SocialImport");
        services.AddOptions<RecipeTextOptions>().BindConfiguration("RecipeText");
        services.AddHttpClient("SocialRedirects", client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Recipoke/1.0");
        }).ConfigurePrimaryHttpMessageHandler(SafeWebsiteClient.CreateHandler);
        services.AddScoped(provider => new SocialUrlResolver(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient("SocialRedirects"),
            SafeWebsiteClient.ValidatePublicHostAsync));
        services.AddHttpClient("GeminiRecipeText", client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
            .RedactLoggedHeaders(["x-goog-api-key"]);
        services.AddSingleton<IRecipeTextModel>(provider => new GeminiRecipeTextModel(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient("GeminiRecipeText"),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RecipeTextOptions>>()));
        services.AddSingleton<RecipeTextExtractor>();
        services.AddSingleton<IMetadataProcessRunner, BoundedProcessRunner>();
        services.AddScoped<ISocialDescriptionRetriever, YtDlpDescriptionRetriever>();
        services.AddScoped<SocialRecipeImporter>();
        services.AddScoped(provider => new RecipeImporterRegistration(
            provider.GetRequiredService<SocialRecipeImporter>(), ImportExecutionPolicy.Social));
        services.AddScoped<JsonLdRecipeExtractor>();
        services.AddScoped<WebsiteRecipeImporter>();
        services.AddScoped(provider => new RecipeImporterRegistration(
            provider.GetRequiredService<WebsiteRecipeImporter>(), ImportExecutionPolicy.Website));
        services.AddScoped<RecipeImportService>();

        return services;
    }
}
