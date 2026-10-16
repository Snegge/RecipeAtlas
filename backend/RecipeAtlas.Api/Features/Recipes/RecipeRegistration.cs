using Recipoke.Api.Features.Recipes.Application;
namespace Recipoke.Api.Features.Recipes;
public static class RecipeRegistration
{
    public static IServiceCollection AddRecipeOperations(this IServiceCollection services) => services
        .AddScoped<CreateRecipe>().AddScoped<UpdateRecipe>().AddScoped<DeleteRecipe>()
        .AddScoped<GetRecipe>().AddScoped<ListRecipes>().AddScoped<GetRecipeImage>()
        .AddScoped<ReplaceRecipeImage>().AddScoped<RemoveRecipeImage>();
}
