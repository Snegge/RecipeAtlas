using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Recipoke.Api.Infrastructure.Persistence.Configurations;

public sealed class RecipeImageConfiguration : IEntityTypeConfiguration<RecipeImage>
{
    public void Configure(EntityTypeBuilder<RecipeImage> model)
    {
        model.HasKey(x => x.RecipeId);
    }
}
