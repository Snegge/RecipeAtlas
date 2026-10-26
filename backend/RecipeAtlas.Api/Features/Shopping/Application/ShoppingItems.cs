using Microsoft.EntityFrameworkCore;
using Recipoke.Domain.Shopping;

namespace Recipoke.Api.Features.Shopping.Application;

public sealed partial class ShoppingOperations
{
    public Task<ShoppingMutationResponse> AddItemAsync(Guid listId, AddShoppingItemInput input, CancellationToken ct = default) =>
        WriteAsync(listId, input.OperationId, "add-item", input, false, null, async (_, actor) =>
        {
            RequireId(input.Id, "id");
            var text = Text(input.Text, 500, "Item text");
            if (await db.ShoppingItems.AnyAsync(x => x.Id == input.Id, ct))
                throw new OperationFailure(409, "This item ID has already been used.", "shopping_id_reused");
            if (await db.ShoppingItems.CountAsync(x => x.ListId == listId && (!x.Deleted || x.UndoUntilUtc != null), ct) >= 1000)
                throw new OperationFailure(409, "A shopping list may contain at most 1000 items.", "shopping_item_limit");
            var position = (await db.ShoppingItems.Where(x => x.ListId == listId).MaxAsync(x => (long?)x.Position, ct) ?? -1) + 1;
            var item = new ShoppingItem { Id = input.Id, ListId = listId, Text = text, Position = position };
            db.ShoppingItems.Add(item);
            return ItemResult(item);
        }, ct);

    public Task<ShoppingMutationResponse> SaveItemAsync(Guid listId, Guid itemId, SaveShoppingItemInput input, CancellationToken ct = default) =>
        WriteAsync(listId, input.OperationId, "save-item", new { itemId, input }, false, new(input.ExpectedVersion), async (_, actor) =>
        {
            var item = await ItemAsync(listId, itemId, ct);
            var lease = await RequireReservationAsync(listId, itemId, actor, input.Token, ct);
            item.Text = Text(input.Text, 500, "Item text");
            Advance(item, new(input.ExpectedVersion));
            db.ShoppingReservations.Remove(lease);
            return ItemResult(item);
        }, ct);

    public Task<ShoppingMutationResponse> SetPurchasedAsync(Guid listId, Guid itemId, PurchasedShoppingItemInput input, CancellationToken ct = default) =>
        WriteAsync(listId, input.OperationId, "purchase-item", new { itemId, input }, false, new(input.ExpectedVersion), async (_, actor) =>
        {
            var item = await ItemAsync(listId, itemId, ct);
            await RequireUnreservedAsync(listId, itemId, ct);
            if (item.Purchased != input.Purchased)
            {
                Advance(item, new(input.ExpectedVersion));
                item.Purchased = input.Purchased;
            }
            return ItemResult(item);
        }, ct);

    public Task<ShoppingMutationResponse> DeleteItemAsync(Guid listId, Guid itemId, VersionPrecondition expected, Guid operationId, CancellationToken ct = default) =>
        WriteAsync(listId, operationId, "delete-item", new { itemId, expected.Value, expected.IsEntityTag }, false, expected, async (_, actor) =>
        {
            var item = await ItemAsync(listId, itemId, ct);
            await RequireUnreservedAsync(listId, itemId, ct);
            Advance(item, expected);
            item.Deleted = true;
            item.DeletedByUserId = actor;
            item.DeletionOperationId = operationId;
            item.UndoUntilUtc = clock.GetUtcNow().AddSeconds(5);
            return new(listId, null, null, item.UndoUntilUtc);
        }, ct);

    public Task<ShoppingMutationResponse> RestoreItemAsync(Guid listId, Guid itemId, RestoreShoppingItemInput input, CancellationToken ct = default) =>
        WriteAsync(listId, input.OperationId, "restore-item", new { itemId, input }, false, null, async (_, actor) =>
        {
            RequireId(input.DeletionOperationId, "deletionOperationId");
            var item = await db.ShoppingItems.SingleOrDefaultAsync(x => x.ListId == listId && x.Id == itemId, ct) ?? throw ShoppingAccess.Unavailable();
            if (item.Deleted && item.UndoUntilUtc > clock.GetUtcNow() && item.DeletedByUserId != actor)
                throw new OperationFailure(403, "Only the person who removed this item may undo that deletion.", "shopping_permission_required");
            if (!item.Deleted || item.DeletionOperationId != input.DeletionOperationId ||
                item.UndoUntilUtc is null || item.UndoUntilUtc <= clock.GetUtcNow())
                throw new OperationFailure(409, "This deletion can no longer be undone.", "shopping_undo_expired");
            Advance(item, new(item.Version.ToString()));
            item.Deleted = false;
            item.DeletedByUserId = null;
            item.DeletionOperationId = null;
            item.UndoUntilUtc = null;
            return ItemResult(item);
        }, ct);

    private async Task<ShoppingItem> ItemAsync(Guid listId, Guid itemId, CancellationToken ct) =>
        await db.ShoppingItems.SingleOrDefaultAsync(x => x.ListId == listId && x.Id == itemId && !x.Deleted, ct) ?? throw ShoppingAccess.Unavailable();

    private async Task RequireUnreservedAsync(Guid listId, Guid itemId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (await db.ShoppingReservations.AnyAsync(x => x.ListId == listId && x.ItemId == itemId && x.ExpiresAtUtc > now, ct))
            throw Reserved();
    }
    private async Task<ShoppingReservation> RequireReservationAsync(Guid listId, Guid itemId, Guid actor, Guid token, CancellationToken ct)
    {
        RequireId(token, "token");
        var now = clock.GetUtcNow();
        return await db.ShoppingReservations.SingleOrDefaultAsync(x => x.ListId == listId && x.ItemId == itemId &&
            x.UserId == actor && x.Token == token && x.ExpiresAtUtc > now, ct) ?? throw ReservationLost();
    }
    private static OperationFailure Reserved() => new(409, "Someone is editing this item. Try again after editing finishes.", "shopping_item_reserved");
    private static OperationFailure ReservationLost() => new(409, "The editing reservation expired or changed. Review the item before trying again.", "shopping_reservation_lost");
}
