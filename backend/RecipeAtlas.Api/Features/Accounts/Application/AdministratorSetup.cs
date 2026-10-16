using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace Recipoke.Api.Features.Accounts.Application;

public sealed class AdministratorSetup(RecipeDb db, UserManager<AppUser> users, TimeProvider clock)
{
    public Task<bool> IsCompleteAsync(CancellationToken ct) => db.AccountSetups.AnyAsync(ct);
    public async Task<bool> BootstrapAsync(string username, string displayName, string password, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await IsCompleteAsync(ct)) return false; // Retry never changes existing credentials or content.
        var profile = AccountValidation.Profile(username, displayName); AccountValidation.Password(password);
        var user = await users.FindByIdAsync(LegacySetup.UserId.ToString()) ?? throw new OperationFailure(409, "The reserved setup account is missing. Restore or repair the database offline.");
        AccountValidation.RequireSuccess(await users.SetUserNameAsync(user, profile.Username));
        user.DisplayName = profile.DisplayName; user.IsActive = true; user.IsAdministrator = true; user.LockoutEnabled = true;
        AccountValidation.RequireSuccess(await users.AddPasswordAsync(user, password));
        var cookbook = await db.Cookbooks.SingleAsync(x => x.Id == LegacySetup.CookbookId, ct);
        cookbook.Name = "Personal cookbook";
        db.AccountSetups.Add(new() { CompletedAtUtc = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true;
    }
    public async Task RecoverAsync(string username, string password, CancellationToken ct)
    {
        AccountValidation.Password(password);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await IsCompleteAsync(ct)) throw new OperationFailure(409, "Complete administrator setup first.");
        var user = await users.FindByNameAsync(username.Trim());
        if (user is null || !user.IsAdministrator) throw new OperationFailure(404, "Administrator not found.");
        var token = await users.GeneratePasswordResetTokenAsync(user);
        user.IsActive = true; user.LockoutEnd = null; user.AccessFailedCount = 0;
        AccountValidation.RequireSuccess(await users.ResetPasswordAsync(user, token, password));
        await tx.CommitAsync(ct);
    }
}
