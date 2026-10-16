using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Infrastructure.Persistence.Configurations;

namespace Recipoke.Api.Infrastructure.Persistence;

public sealed class RecipeDb(DbContextOptions<RecipeDb> options) : DbContext(options)
{
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeImage> Images => Set<RecipeImage>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ApplyConfiguration(new RecipeConfiguration());
        model.ApplyConfiguration(new IngredientConfiguration());
        model.ApplyConfiguration(new RecipeStepConfiguration());
        model.ApplyConfiguration(new RecipeImageConfiguration());
    }
}
