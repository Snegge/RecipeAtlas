using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Recipoke.Api.Infrastructure.Persistence.Configurations;

public sealed class RecipeConfiguration : IEntityTypeConfiguration<Recipe>
{
    public void Configure(EntityTypeBuilder<Recipe> model)
    {
        model.HasKey(x => x.Id);
        model.Property(x => x.Title).HasMaxLength(200);
        model.Property(x => x.Description).HasMaxLength(4000);
        model.Property(x => x.SourceUrl).HasMaxLength(2048);
        model.HasIndex(x => x.UpdatedAtUtc);
        model.HasMany(x => x.Ingredients).WithOne().HasForeignKey(x => x.RecipeId).OnDelete(DeleteBehavior.Cascade);
        model.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.RecipeId).OnDelete(DeleteBehavior.Cascade);
        model.HasOne(x => x.Image).WithOne().HasForeignKey<RecipeImage>(x => x.RecipeId).OnDelete(DeleteBehavior.Cascade);
    }
}
