using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace Recipoke.Api.Features.Accounts.Application;

public sealed class AdministratorAccounts(RecipeDb db, AccountAccess access, UserManager<AppUser> users)
{
    public async Task<IReadOnlyList<AccountResponse>> ListAsync(CancellationToken ct)
    {
        await access.RequireAdministratorAsync(ct);
        return await db.Users.AsNoTracking().OrderBy(x => x.NormalizedUserName)
            .Select(x => new AccountResponse(x.Id, x.UserName!, x.DisplayName, x.IsActive, x.IsAdministrator, x.Email, x.ProfileImage == null ? null : "/api/accounts/" + x.Id + "/avatar?v=" + x.ProfileImage.Version)).ToListAsync(ct);
    }
    public async Task SetStatusAsync(Guid id, bool active, CancellationToken ct)
    {
        await using var tx = await WriteTransaction.BeginAsync(db, ct);
        await access.RequireAdministratorAsync(ct);
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new OperationFailure(404, "Account not found.");
        if (user.IsAdministrator) throw new OperationFailure(403, "Administrator accounts require server-side recovery.");
        if (user.IsActive != active)
        {
            user.IsActive = active;
            AccountValidation.RequireSuccess(await users.UpdateSecurityStampAsync(user));
        }
        await tx.CommitAsync(ct);
    }
}
