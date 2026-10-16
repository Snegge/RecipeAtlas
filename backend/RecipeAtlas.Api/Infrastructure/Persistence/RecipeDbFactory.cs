using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Recipoke.Api.Infrastructure.Persistence;

// Lets dotnet-ef generate migrations without starting the web app or needing a password.
public sealed class RecipeDbFactory : IDesignTimeDbContextFactory<RecipeDb>
{
    public RecipeDb CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<RecipeDb>().UseSqlite("Data Source=design-time.db").Options);
}
