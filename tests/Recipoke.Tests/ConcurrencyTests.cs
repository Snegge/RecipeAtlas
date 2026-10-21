using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Recipoke.Api.Common.Application;

internal static class ConcurrencyTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "recipoke-cas-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var connection = "Data Source=" + Path.Combine(directory, "test.db");
        var options = new DbContextOptionsBuilder<RecipeDb>().UseSqlite(connection).Options;
        var actor = new TestCurrentUser(LegacySetup.UserId);
        RecipeInput input = new("Original", null, null, 2, [new("Eggs", "1/2-1 1/2", "piece", null)], ["First", "Second"]);
        try
        {
            Guid id; Guid firstVersion;
            await using (var db = new RecipeDb(options))
            {
                await db.Database.MigrateAsync();
                (await db.Users.SingleAsync()).IsActive = true; await db.SaveChangesAsync();
                var created = (await new CreateRecipe(db, new CookbookAccess(db, new AccountAccess(db, actor)))
                    .ExecuteAsync(LegacySetup.CookbookId, input, default)).Recipe!;
                id = created.Id; firstVersion = created.Version;
                check(db.Model.FindEntityType(typeof(Recipe))!.FindProperty("Version")!.IsConcurrencyToken, "Recipe CAS is enforced by EF metadata");
                check(db.Model.FindEntityType(typeof(Cookbook))!.FindProperty("Version")!.IsConcurrencyToken, "Cookbook CAS is enforced by EF metadata");
            }
            // Independent connections retain their own original versions. No read-before-write substitute.
            await using (var a = new RecipeDb(options))
            await using (var b = new RecipeDb(options))
            {
                var first = await a.Recipes.SingleAsync(); var second = await b.Recipes.SingleAsync();
                await using (var tx = await a.Database.BeginTransactionAsync(IsolationLevel.Serializable))
                {
                    ConcurrencyWrites.Advance(a.Entry(first).Property(x => x.Version), new(firstVersion.ToString()));
                    first.Title = "Winner"; await a.SaveChangesAsync(); await tx.CommitAsync();
                }
                await using var losing = await b.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                ConcurrencyWrites.Advance(b.Entry(second).Property(x => x.Version), new(firstVersion.ToString()));
                second.Title = "Rejected";
                // Child writes precede/follow CAS according to EF ordering; all must roll back.
                b.Set<RecipeStep>().RemoveRange(await b.Set<RecipeStep>().ToListAsync());
                var rechecked = false;
                try
                {
                    await ConcurrencyWrites.SaveAsync(b, new(firstVersion.ToString()), async () =>
                    {
                        rechecked = true;
                        check(!b.ChangeTracker.Entries().Any(), "Failed CAS clears tracked stale authorization and content");
                        await new CookbookAccess(b, new AccountAccess(b, actor)).RequireAsync(LegacySetup.CookbookId, CookbookPermission.Write, default);
                    }, default);
                    throw new Exception("Stale direct EF update accepted");
                }
                catch (OperationFailure failure) { check(failure.StatusCode == 409 && failure.Code == "concurrency_conflict" && rechecked, "Failed DB CAS rechecks current access before body conflict"); }
            }
            await using (var verify = new RecipeDb(options))
            {
                var recipe = await verify.Recipes.Include(x => x.Steps).SingleAsync();
                check(recipe.Title == "Winner" && recipe.Steps.Count == 2, "Rejected CAS preserves winning parent and complete child state");
            }
            // Deterministic permission change precisely after failed-transaction rollback.
            var revoke = new RevokeAfterRollback(connection, actor.UserId!.Value);
            var revokedOptions = new DbContextOptionsBuilder<RecipeDb>().UseSqlite(connection).AddInterceptors(revoke).Options;
            await using (var db = new RecipeDb(revokedOptions))
            {
                var access = new CookbookAccess(db, new AccountAccess(db, actor));
                try { await new UpdateRecipe(db, access).ExecuteAsync(LegacySetup.CookbookId, id, input with {ExpectedVersion = firstVersion.ToString()}, default); throw new Exception("Deactivated user received ordinary conflict"); }
                catch (OperationFailure failure) { check(revoke.Executed && failure.StatusCode == 401, "Fresh post-rollback authorization beats concurrency classification after deactivation"); }
            }
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }
    private sealed class RevokeAfterRollback(string connection, Guid userId) : DbTransactionInterceptor
    {
        public bool Executed;
        public override async Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Executed) return;
            Executed = true;
            await using var db = new RecipeDb(new DbContextOptionsBuilder<RecipeDb>().UseSqlite(connection).Options);
            await db.Users.Where(x => x.Id == userId).ExecuteUpdateAsync(set => set.SetProperty(x => x.IsActive, false), cancellationToken);
        }
    }
}
