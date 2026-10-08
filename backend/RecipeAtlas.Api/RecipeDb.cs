using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RecipeAtlas.Api;

public sealed class RecipeDb(DbContextOptions<RecipeDb> options) : DbContext(options)
{
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeImage> Images => Set<RecipeImage>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var recipe = model.Entity<Recipe>();
        recipe.HasKey(x => x.Id);
        recipe.Property(x => x.Title).HasMaxLength(200);
        recipe.Property(x => x.Description).HasMaxLength(4000);
        recipe.Property(x => x.SourceUrl).HasMaxLength(2048);
        recipe.HasIndex(x => x.UpdatedAtUtc);
        recipe.HasMany(x => x.Ingredients).WithOne().HasForeignKey(x => x.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);
        recipe.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);
        recipe.HasOne(x => x.Image).WithOne().HasForeignKey<RecipeImage>(x => x.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);
        model.Entity<RecipeImage>().HasKey(x => x.RecipeId);
        model.Entity<Ingredient>().Property(x => x.Name).HasMaxLength(200);
        model.Entity<Ingredient>().Property(x => x.Note).HasMaxLength(300);
        model.Entity<Ingredient>().Property(x => x.Unit).HasMaxLength(16);
        model.Entity<RecipeStep>().Property(x => x.Instruction).HasMaxLength(4000);
    }
}

// Lets dotnet-ef generate migrations without starting the web app or needing a password.
public sealed class RecipeDbFactory : IDesignTimeDbContextFactory<RecipeDb>
{
    public RecipeDb CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<RecipeDb>().UseSqlite("Data Source=design-time.db").Options);
}
