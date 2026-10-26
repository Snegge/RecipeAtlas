using Microsoft.EntityFrameworkCore;
using Recipoke.Domain.Shopping;

namespace Recipoke.Api.Features.Shopping.Application;

public sealed partial class ShoppingOperations
{
    public async Task<IReadOnlyList<ShoppingListSummary>> ListAsync(CancellationToken ct = default)
    {
        await using var snapshot = await ReadSnapshot.BeginAsync(db, ct);
        var actor = await accounts.RequireActiveAsync(ct);
        var lists = await db.ShoppingLists.AsNoTracking().Where(x => x.OwnerUserId == actor.Id ||
            db.ShoppingMemberships.Any(m => m.ListId == x.Id && m.UserId == actor.Id)).OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);
        var result = new List<ShoppingListSummary>();
        foreach (var list in lists)
        {
            var items = db.ShoppingItems.AsNoTracking().Where(x => x.ListId == list.Id && !x.Deleted);
            result.Add(new(list.Id, list.Name, list.OwnerUserId == actor.Id ? "Owner" : "Member", list.Version,
                await items.Where(x => !x.Purchased).OrderBy(x => x.Position).Take(4).Select(x => x.Text).ToArrayAsync(ct),
                await items.CountAsync(x => !x.Purchased, ct), await items.CountAsync(x => x.Purchased, ct)));
        }
        return result;
    }

    public async Task<ShoppingListResponse> GetAsync(Guid listId, CancellationToken ct = default)
    {
        await using var snapshot = await ReadSnapshot.BeginAsync(db, ct);
        var role = await access.RequireAsync(listId, false, ct);
        var list = await db.ShoppingLists.AsNoTracking().SingleAsync(x => x.Id == listId, ct);
        var items = await db.ShoppingItems.AsNoTracking().Where(x => x.ListId == listId && !x.Deleted).OrderBy(x => x.Position).ToArrayAsync(ct);
        var now = clock.GetUtcNow();
        var leases = await db.ShoppingReservations.AsNoTracking().Where(x => x.ListId == listId && x.ExpiresAtUtc > now)
            .Join(db.Users, r => r.UserId, u => u.Id, (r, u) => new { r.ItemId, r.ExpiresAtUtc, u.DisplayName }).ToDictionaryAsync(x => x.ItemId, ct);
        var members = await db.Users.AsNoTracking().Where(x => x.Id == list.OwnerUserId || db.ShoppingMemberships.Any(m => m.ListId == listId && m.UserId == x.Id))
            .OrderBy(x => x.NormalizedUserName).Select(x => new ShoppingMemberResponse(x.Id, x.UserName!, x.DisplayName, x.Id == list.OwnerUserId ? "Owner" : "Member")).ToArrayAsync(ct);
        var invitations = role == ShoppingRole.Owner
            ? await db.ShoppingInvitations.AsNoTracking().Where(x => x.ListId == listId).Join(db.Users, i => i.UserId, u => u.Id,
                (i, u) => new ShoppingPendingInvitation(i.Id, u.Id, u.UserName!, u.DisplayName)).ToArrayAsync(ct)
            : [];
        return new(list.Id, list.Name, role.ToString(), list.Version, items.Select(item =>
        {
            leases.TryGetValue(item.Id, out var lease);
            return new ShoppingItemResponse(item.Id, item.Text, item.Purchased, item.Version, item.Position, lease?.DisplayName, lease?.ExpiresAtUtc);
        }).ToArray(), members, invitations);
    }

    public async Task<IReadOnlyList<ShoppingInvitationResponse>> InboxAsync(CancellationToken ct = default)
    {
        await using var snapshot = await ReadSnapshot.BeginAsync(db, ct);
        var actor = await accounts.RequireActiveAsync(ct);
        return await db.ShoppingInvitations.AsNoTracking().Where(x => x.UserId == actor.Id)
            .Join(db.ShoppingLists, i => i.ListId, l => l.Id, (i, l) => new { Invitation = i, List = l })
            .Join(db.Users, x => x.List.OwnerUserId, u => u.Id, (x, u) => new ShoppingInvitationResponse(
                x.Invitation.Id, x.List.Id, x.List.Name, u.DisplayName, x.List.Version)).ToArrayAsync(ct);
    }
}
