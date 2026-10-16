using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Recipoke.Api.Infrastructure.Persistence.Configurations;

public sealed class IngredientConfiguration : IEntityTypeConfiguration<Ingredient>
{
    public void Configure(EntityTypeBuilder<Ingredient> model)
    {
        model.Property(x => x.Name).HasMaxLength(200);
        model.Property(x => x.Note).HasMaxLength(300);
        model.Property(x => x.Unit).HasMaxLength(16);
        model.Property(x => x.Quantity).HasMaxLength(64).IsRequired();
    }
}
