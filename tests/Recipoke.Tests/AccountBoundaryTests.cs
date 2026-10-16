using System.Data.Common;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Recipoke.Api.Features.Accounts.Application;
using Recipoke.Domain.Accounts;

internal static class AccountBoundaryTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var now = DateTimeOffset.Parse("2026-10-16T12:00:00Z");
        check(InvitationRules.Status(now, now.AddHours(72), null, null) == "Pending", "Pending invitation");
        check(InvitationRules.Status(now, now, null, null) == "Expired", "Expiry boundary is exclusive");
        check(InvitationRules.Status(now, now.AddDays(1), now, null) == "Revoked", "Revocation beats expiry");
        check(InvitationRules.Status(now, now.AddDays(1), null, now) == "Consumed", "Consumed invitation cannot be reused");
        foreach (var role in new[] { CookbookRole.Owner, CookbookRole.Editor, CookbookRole.Viewer })
        {
            check(CookbookPermissions.Allows(role, CookbookPermission.Read), "Member role may read");
            check(CookbookPermissions.Allows(role, CookbookPermission.Manage) == (role == CookbookRole.Owner), "Only owner manages membership");
            check(CookbookPermissions.Allows(role, CookbookPermission.Write) == (role != CookbookRole.Viewer), "Viewer is read-only");
        }
        check(!CookbookPermissions.Allows((CookbookRole)99, CookbookPermission.Read), "Undefined roles never grant access");
        var directory = Path.Combine(Path.GetTempPath(), "recipoke-account-boundaries-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var fail = new FailSecondWrite();
            var connection = "Data Source=" + Path.Combine(directory, "accounts.db");
            var services = new ServiceCollection(); services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(directory, "keys")));
            services.AddDbContext<RecipeDb>(o => o.UseSqlite(connection).AddInterceptors(fail));
            services.AddIdentityCore<AppUser>(o => { o.Password.RequiredLength = 12; o.Password.RequireNonAlphanumeric = false; o.Password.RequireUppercase = false; o.Password.RequireLowercase = false; o.Password.RequireDigit = false; })
                .AddEntityFrameworkStores<RecipeDb>().AddDefaultTokenProviders();
            services.AddScoped<AdministratorSetup>();
            await using var provider = services.BuildServiceProvider();
            await using (var scope = provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<RecipeDb>().Database.MigrateAsync();
            fail.Enabled = true;
            await using (var scope = provider.CreateAsyncScope())
            {
                try { await scope.ServiceProvider.GetRequiredService<AdministratorSetup>().BootstrapAsync("administrator", "Administrator", "isolated-test-password", default); throw new Exception("Expected setup write failure"); }
                catch (DbUpdateException) { check(fail.Writes > 0, "Bootstrap failure follows a real write"); }
            }
            fail.Enabled = false;
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RecipeDb>();
                check(!await db.AccountSetups.AnyAsync(), "Interrupted bootstrap remains closed");
                var pending = await db.Users.SingleAsync();
                check(!pending.IsActive && pending.PasswordHash is null && pending.UserName == "__setup_pending__", "Bootstrap partial identity changes rolled back");
                var setup = scope.ServiceProvider.GetRequiredService<AdministratorSetup>();
                check(await setup.BootstrapAsync("administrator", "Administrator", "isolated-test-password", default), "Bootstrap succeeds after interruption");
                check(!await setup.BootstrapAsync("different", "Other", "different-test-password", default), "Completed bootstrap refuses reinitialisation");
                check(await db.Users.CountAsync() == 1 && await db.Cookbooks.CountAsync() == 1, "Retry does not duplicate administrator/cookbook");
                check((await db.Users.SingleAsync()).LockoutEnabled, "Bootstrap enables framework lockout for the administrator too");
            }
            await TestWriteOrdering(connection, check);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }
    private static async Task TestWriteOrdering(string connection, Action<bool, string> check)
    {
        var options = new DbContextOptionsBuilder<RecipeDb>().UseSqlite(connection).Options;
        var editorId = Guid.NewGuid(); var bookId = LegacySetup.CookbookId;
        Guid recipeId;
        RecipeInput input = new("Before", null, null, 2, [new("Eggs", "2", "piece", null)], ["Cook"]);
        await using (var db = new RecipeDb(options))
        {
            db.Users.Add(new AppUser { Id = editorId, UserName = "editor", NormalizedUserName = "EDITOR", DisplayName = "Editor", IsActive = true });
            db.Memberships.Add(new() { CookbookId = bookId, UserId = editorId, Role = CookbookRole.Editor }); await db.SaveChangesAsync();
            var access = new CookbookAccess(db, new AccountAccess(db, new TestCurrentUser(LegacySetup.UserId)));
            recipeId = (await new CreateRecipe(db, access).ExecuteAsync(bookId, input, default)).Recipe!.Id;
        }
        var pause = new PauseAfterPermissionRead();
        var pausedOptions = new DbContextOptionsBuilder<RecipeDb>().UseSqlite(connection).AddInterceptors(pause).Options;
        var writing = Task.Run(async () =>
        {
            await using var db = new RecipeDb(pausedOptions);
            var access = new CookbookAccess(db, new AccountAccess(db, new TestCurrentUser(editorId)));
            return await new UpdateRecipe(db, access).ExecuteAsync(bookId, recipeId, input with { Title = "Committed before downgrade" }, default);
        });
        await pause.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var downgrade = Task.Run(async () =>
        {
            await using var db = new RecipeDb(options);
            var accounts = new AccountAccess(db, new TestCurrentUser(LegacySetup.UserId));
            var access = new CookbookAccess(db, accounts);
            await new CookbookOperations(db, accounts, access, new UpperInvariantLookupNormalizer()).ChangeMemberAsync(bookId, editorId, "Viewer", false, default);
        });
        try { await Task.Delay(100); check(!downgrade.IsCompleted, "Permission change cannot overtake an authorized write transaction"); }
        finally { pause.Release.TrySetResult(); }
        await writing; await downgrade;
        await using (var db = new RecipeDb(options))
        {
            var access = new CookbookAccess(db, new AccountAccess(db, new TestCurrentUser(editorId)));
            try { await new UpdateRecipe(db, access).ExecuteAsync(bookId, recipeId, input, default); throw new Exception("Downgraded writer accepted"); }
            catch (Recipoke.Api.Common.Application.OperationFailure e) { check(e.StatusCode == 403, "Write after committed downgrade is denied"); }
            check((await db.Recipes.SingleAsync()).Title == "Committed before downgrade", "Transaction ordering preserves the authorized earlier write");
        }
    }
    private sealed class FailSecondWrite : DbCommandInterceptor
    {
        public bool Enabled; public int Writes;
        private static bool IsWrite(DbCommand command) => new[] { "UPDATE", "INSERT", "DELETE" }.Any(x => command.CommandText.TrimStart().StartsWith(x));
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { if (Enabled && IsWrite(command) && Writes > 0) throw new InvalidOperationException("Injected setup failure"); return base.ReaderExecutingAsync(command, eventData, result, cancellationToken); }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        { if (Enabled && IsWrite(command)) Writes++; return base.ReaderExecutedAsync(command, eventData, result, cancellationToken); }
    }
    private sealed class PauseAfterPermissionRead : DbCommandInterceptor
    {
        public readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Memberships\"")) { Ready.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            return result;
        }
    }
}
