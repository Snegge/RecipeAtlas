using Microsoft.EntityFrameworkCore;
namespace Recipoke.Api.Features.Authentication.Application;

public interface ICurrentUser { Guid? UserId { get; } }
public sealed class AccountAccess(RecipeDb db, ICurrentUser current)
{
    public async Task<AppUser> RequireActiveAsync(CancellationToken ct)
    {
        var user = current.UserId is { } id ? await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) : null;
        if (user is null || !user.IsActive) throw new OperationFailure(401, "Sign in to continue.", "authentication_required");
        return user;
    }
    public async Task<AppUser> RequireAdministratorAsync(CancellationToken ct)
    {
        var user = await RequireActiveAsync(ct);
        if (!user.IsAdministrator) throw new OperationFailure(403, "Administrator access is required.", "administrator_required");
        return user;
    }
}
