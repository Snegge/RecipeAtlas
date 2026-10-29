using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Recipoke.Api.Features.Shopping.Application;

public sealed partial class ShoppingOperations
{
    public async Task<ShoppingReservationResponse> AcquireAsync(Guid listId, Guid itemId, AcquireShoppingReservationInput input, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var actor = await accounts.RequireActiveAsync(ct);
        await access.RequireAsync(listId, false, ct);
        RequireId(input.Token, "token");
        var item = await ItemAsync(listId, itemId, ct);
        var expected = new VersionPrecondition(input.ExpectedVersion);
        if (item.Version != expected.Require()) throw expected.Conflict();
        var now = clock.GetUtcNow();
        var lease = await db.ShoppingReservations.SingleOrDefaultAsync(x => x.ItemId == itemId && x.ListId == listId, ct);
        if (lease is not null && lease.ExpiresAtUtc > now)
        {
            if (lease.UserId == actor.Id && lease.Token == input.Token) return new(lease.Token, lease.ExpiresAtUtc);
            throw Reserved();
        }
        if (await db.ShoppingReservationTokens.AnyAsync(x => x.ItemId == itemId && x.Token == input.Token, ct)) throw ReservationLost();
        if (lease is null)
        {
            lease = new() { ListId = listId, ItemId = itemId };
            db.ShoppingReservations.Add(lease);
        }
        lease.UserId = actor.Id;
        lease.Token = input.Token;
        lease.ExpiresAtUtc = now.AddSeconds(15);
        db.ShoppingReservationTokens.Add(new() { ItemId = itemId, Token = input.Token });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await NotifyAsync(listId);
        return new(lease.Token, lease.ExpiresAtUtc);
    }

    public async Task ReleaseAsync(Guid listId, Guid itemId, Guid token, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var actor = await accounts.RequireActiveAsync(ct);
        await access.RequireAsync(listId, false, ct);
        await ItemAsync(listId, itemId, ct);
        var lease = await RequireReservationAsync(listId, itemId, actor.Id, token, ct);
        db.ShoppingReservations.Remove(lease);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await NotifyAsync(listId);
    }
}
