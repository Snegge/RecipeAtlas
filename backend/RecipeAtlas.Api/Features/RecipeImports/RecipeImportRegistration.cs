using System.Net.Http.Headers;

namespace RecipeAtlas.Api.Features.RecipeImport;

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
                "RecipeAtlas/1.0");

            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("text/html"));
        })
        .ConfigurePrimaryHttpMessageHandler(
            SafeWebsiteClient.CreateHandler);

        services.AddScoped<JsonLdRecipeExtractor>();
        services.AddScoped<IRecipeImporter, WebsiteRecipeImporter>();
        services.AddScoped<RecipeImportService>();

        return services;
    }
}