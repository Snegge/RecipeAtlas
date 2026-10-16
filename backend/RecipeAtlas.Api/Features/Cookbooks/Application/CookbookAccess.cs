using Microsoft.EntityFrameworkCore;
namespace Recipoke.Api.Features.Cookbooks.Application;

public sealed class CookbookAccess(RecipeDb db, AccountAccess accounts)
{
    public async Task<CookbookRole> RequireAsync(Guid cookbookId, CookbookPermission permission, CancellationToken ct)
    {
        var user = await accounts.RequireActiveAsync(ct);
        var cookbook = await db.Cookbooks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == cookbookId, ct);
        if (cookbook is null) throw new OperationFailure(404, "Cookbook not found.");
        CookbookRole? role = cookbook.OwnerUserId == user.Id ? CookbookRole.Owner :
            await db.Memberships.Where(x => x.CookbookId == cookbookId && x.UserId == user.Id).Select(x => (CookbookRole?)x.Role).SingleOrDefaultAsync(ct);
        if (role is null) throw new OperationFailure(404, "Cookbook not found.");
        if (!CookbookPermissions.Allows(role.Value, permission)) throw new OperationFailure(403, "You do not have permission for this cookbook.", "cookbook_permission_required");
        return role.Value;
    }
}
