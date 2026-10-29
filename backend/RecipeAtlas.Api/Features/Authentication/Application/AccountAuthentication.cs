using Microsoft.EntityFrameworkCore;
using System.Data;
using Microsoft.AspNetCore.Identity;
using Recipoke.Api.Features.Accounts.Application;
namespace Recipoke.Api.Features.Authentication.Application;

public sealed class AccountAuthentication(RecipeDb db, UserManager<AppUser> users, SignInManager<AppUser> signIn, AccountAccess access, AccountProfiles profiles)
{
    public async Task<Guid?> AuthenticateAsync(string? username, string? password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username) || password is null || password.Length > 256) return null;
        await using var tx = await WriteTransaction.BeginAsync(db, ct);
        var user = await users.FindByNameAsync(username.Trim());
        if (user is null) return null;
        await db.Entry(user).ReloadAsync(ct);
        if (!user.IsActive) return null;
        var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        await tx.CommitAsync(ct);
        return result.Succeeded ? user.Id : null;
    }
    public async Task<CurrentUserResponse> CurrentAsync(CancellationToken ct) => await profiles.CurrentAsync(ct);
    public async Task<Guid> ChangePasswordAsync(ChangePasswordInput input, CancellationToken ct)
    {
        AccountValidation.Password(input.NewPassword);
        await using var tx = await WriteTransaction.BeginAsync(db, ct);
        var active = await access.RequireActiveAsync(ct);
        var user = await users.FindByIdAsync(active.Id.ToString());
        await db.Entry(user!).ReloadAsync(ct);
        AccountValidation.RequireSuccess(await users.ChangePasswordAsync(user!, input.CurrentPassword ?? "", input.NewPassword!));
        await tx.CommitAsync(ct);
        return active.Id;
    }
}
