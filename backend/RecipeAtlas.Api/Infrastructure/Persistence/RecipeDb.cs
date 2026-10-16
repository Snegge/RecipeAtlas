using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Recipoke.Domain.Cookbooks;
using Recipoke.Api.Infrastructure.Persistence.Configurations;

namespace Recipoke.Api.Infrastructure.Persistence;

public sealed class RecipeDb(DbContextOptions<RecipeDb> options) : IdentityUserContext<AppUser, Guid>(options)
{
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeImage> Images => Set<RecipeImage>();
    public DbSet<Cookbook> Cookbooks => Set<Cookbook>();
    public DbSet<CookbookMembership> Memberships => Set<CookbookMembership>();
    public DbSet<AccountInvitation> Invitations => Set<AccountInvitation>();
    public DbSet<AccountSetup> AccountSetups => Set<AccountSetup>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<AppUser>().Property(x => x.DisplayName).HasMaxLength(100);
        model.Entity<AppUser>().Property(x => x.UserName).IsRequired();
        model.Entity<AppUser>().Property(x => x.NormalizedUserName).IsRequired();
        model.Entity<Cookbook>().Property(x => x.Name).HasMaxLength(100);
        model.Entity<Cookbook>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CookbookMembership>().HasKey(x => new { x.CookbookId, x.UserId });
        model.Entity<CookbookMembership>().HasOne<Cookbook>().WithMany().HasForeignKey(x => x.CookbookId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CookbookMembership>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CookbookMembership>().ToTable(t => t.HasCheckConstraint("CK_Membership_Role", "Role IN (1, 2)"));
        model.Entity<AccountInvitation>().Property(x => x.CreatedAtUtc).HasConversion<long>();
        model.Entity<AccountInvitation>().Property(x => x.ExpiresAtUtc).HasConversion<long>();
        model.Entity<AccountInvitation>().Property(x => x.RevokedAtUtc).HasConversion<long?>();
        model.Entity<AccountInvitation>().Property(x => x.ConsumedAtUtc).HasConversion<long?>();
        model.Entity<AccountSetup>().Property(x => x.CompletedAtUtc).HasConversion<long>();
        model.Entity<AccountInvitation>().HasIndex(x => x.TokenHash).IsUnique();
        model.Entity<AccountInvitation>().Property(x => x.TokenHash).HasMaxLength(64);
        model.Entity<AccountInvitation>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.IssuedByUserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<AccountInvitation>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.ConsumedByUserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<AccountSetup>().ToTable(t => t.HasCheckConstraint("CK_AccountSetup_Singleton", "Id = 1"));
        model.Entity<Recipe>().HasOne<Cookbook>().WithMany().HasForeignKey(x => x.CookbookId).OnDelete(DeleteBehavior.Restrict);
        model.ApplyConfiguration(new RecipeConfiguration());
        model.ApplyConfiguration(new IngredientConfiguration());
        model.ApplyConfiguration(new RecipeStepConfiguration());
        model.ApplyConfiguration(new RecipeImageConfiguration());
    }
}
