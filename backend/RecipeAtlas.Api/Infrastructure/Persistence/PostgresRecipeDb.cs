using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Recipoke.Api.Infrastructure.Persistence;

public sealed class PostgresRecipeDb(DbContextOptions<PostgresRecipeDb> options) : RecipeDb(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<DataProtectionKey>().ToTable("DataProtectionKeys");
        model.Entity<CookbookInvitation>().ToTable(t => t.HasCheckConstraint("CK_CookbookInvitation_Role", "\"Role\" IN (1, 2)"));
        model.Entity<CookbookMembership>().ToTable(t => t.HasCheckConstraint("CK_Membership_Role", "\"Role\" IN (1, 2)"));
        model.Entity<AccountSetup>().ToTable(t => t.HasCheckConstraint("CK_AccountSetup_Singleton", "\"Id\" = 1"));
    }
}

public sealed class PostgresRecipeDbFactory : IDesignTimeDbContextFactory<PostgresRecipeDb>
{
    public PostgresRecipeDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<PostgresRecipeDb>()
        .UseNpgsql("Host=localhost;Database=design_only;Username=design_only").Options);
}
