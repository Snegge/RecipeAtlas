using Microsoft.EntityFrameworkCore;
using Recipoke.Domain.Shopping;

namespace Recipoke.Api.Features.Shopping.Application;

public sealed class ShoppingAccess(RecipeDb db, AccountAccess accounts)
{
    public static Task<bool> CanReadAsync(RecipeDb db, Guid listId, Guid userId, CancellationToken ct) =>
        db.ShoppingLists.AsNoTracking().AnyAsync(x => x.Id == listId && (x.OwnerUserId == userId ||
            db.ShoppingMemberships.Any(m => m.ListId == listId && m.UserId == userId)), ct);

    public async Task<ShoppingRole> RequireAsync(Guid listId, bool ownerOnly, CancellationToken ct)
    {
        var actor = await accounts.RequireActiveAsync(ct);
        var list = await db.ShoppingLists.AsNoTracking().SingleOrDefaultAsync(x => x.Id == listId, ct);
        if (list is null) throw Unavailable();
        var role = list.OwnerUserId == actor.Id ? ShoppingRole.Owner :
            await db.ShoppingMemberships.AnyAsync(x => x.ListId == listId && x.UserId == actor.Id, ct)
                ? ShoppingRole.Member : (ShoppingRole?)null;
        if (role is null) throw Unavailable();
        if (ownerOnly && role != ShoppingRole.Owner) throw new OperationFailure(403, "Only the owner may manage this shopping list.", "shopping_owner_required");
        return role.Value;
    }

    internal static OperationFailure Unavailable() => new(404, "Shopping list or item is unavailable.", "resource_unavailable");
}
