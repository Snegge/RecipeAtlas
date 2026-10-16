using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Recipoke.Api.Infrastructure.Persistence.Configurations;

public sealed class RecipeStepConfiguration : IEntityTypeConfiguration<RecipeStep>
{
    public void Configure(EntityTypeBuilder<RecipeStep> model)
    {
        model.Property(x => x.Instruction).HasMaxLength(4000);
    }
}
