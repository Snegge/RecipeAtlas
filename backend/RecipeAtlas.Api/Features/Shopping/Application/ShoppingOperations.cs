using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Recipoke.Domain.Shopping;

namespace Recipoke.Api.Features.Shopping.Application;

public sealed partial class ShoppingOperations(RecipeDb db, AccountAccess accounts, ShoppingAccess access,
    ILookupNormalizer normalizer, TimeProvider clock, IShoppingNotifications notifications, ILogger<ShoppingOperations> logger)
{
    private async Task<ShoppingMutationResponse> WriteAsync(Guid listId, Guid operationId, string operation, object payload,
        bool ownerOnly, VersionPrecondition? expected, Func<ShoppingList, Guid, Task<ShoppingMutationResponse>> change, CancellationToken ct,
        Guid? permittedSelfRemoval = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var actor = await accounts.RequireActiveAsync(ct);
        var requireOwner = ownerOnly && actor.Id != permittedSelfRemoval;
        await access.RequireAsync(listId, requireOwner, ct);
        var replay = await ReplayAsync(listId, actor.Id, operationId, operation, payload, ct);
        if (replay is not null) return replay;
        expected?.Require();
        await PurgeRecoveryAsync(listId, ct);
        var list = await db.ShoppingLists.SingleAsync(x => x.Id == listId, ct);
        var result = await change(list, actor.Id);
        Receipt(listId, actor.Id, operationId, operation, payload, result);
        if (expected is { } precondition)
            await ConcurrencyWrites.SaveAsync(db, precondition, () => access.RequireAsync(listId, requireOwner, ct), ct);
        else await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await NotifyAsync(listId);
        return result;
    }

    private async Task<ShoppingMutationResponse?> ReplayAsync(Guid listId, Guid actor, Guid operationId,
        string operation, object payload, CancellationToken ct)
    {
        RequireId(operationId, "operationId");
        var receipt = await db.ShoppingReceipts.AsNoTracking().SingleOrDefaultAsync(
            x => x.ListId == listId && x.ActorUserId == actor && x.OperationId == operationId, ct);
        if (receipt is null) return null;
        if (receipt.Operation != operation || receipt.PayloadHash != Hash(payload))
            throw new OperationFailure(409, "This operation ID was already used with another request.", "operation_id_reused");
        return JsonSerializer.Deserialize<ShoppingMutationResponse>(receipt.ResultJson)!;
    }

    private void Receipt(Guid listId, Guid actor, Guid operationId, string operation, object payload, ShoppingMutationResponse result) =>
        db.ShoppingReceipts.Add(new() { ListId = listId, ActorUserId = actor, OperationId = operationId,
            Operation = operation, PayloadHash = Hash(payload), ResultJson = JsonSerializer.Serialize(result) });

    private static string Hash(object payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
    private static void RequireId(Guid id, string field)
    {
        if (id == Guid.Empty) throw new OperationFailure(400, $"A nonempty UUID is required for {field}.", "invalid_shopping_input");
    }
    private static string Text(string? value, int maximum, string field)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > maximum)
            throw new OperationFailure(400, $"{field} must contain 1 to {maximum} characters.", "invalid_shopping_input");
        return text;
    }
    private void Advance(ShoppingList list, VersionPrecondition expected) => ConcurrencyWrites.Advance(db.Entry(list).Property(x => x.Version), expected);
    private void Advance(ShoppingItem item, VersionPrecondition expected) => ConcurrencyWrites.Advance(db.Entry(item).Property(x => x.Version), expected);
    private static ShoppingMutationResponse Administrative(ShoppingList list) => new(list.Id, list.Version, null, null);
    private static ShoppingMutationResponse ItemResult(ShoppingItem item) => new(item.ListId, null,
        new(item.Id, item.Text, item.Purchased, item.Version, item.Position, null, null), null);

    private Task<int> PurgeRecoveryAsync(Guid listId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.ShoppingItems.Where(x => x.ListId == listId && x.Deleted && x.UndoUntilUtc <= now)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Text, "").SetProperty(x => x.Purchased, false)
                .SetProperty(x => x.DeletedByUserId, (Guid?)null).SetProperty(x => x.DeletionOperationId, (Guid?)null)
                .SetProperty(x => x.UndoUntilUtc, (DateTimeOffset?)null), ct);
    }
    private async Task NotifyAsync(Guid listId)
    {
        // The durable write has committed; delivery outages are repaired by the client's recovery refresh.
        try { await notifications.NotifyAsync(listId, CancellationToken.None); }
        catch (Exception failure) { logger.LogWarning(failure, "Shopping notification delivery failed after commit for {ListId}", listId); }
    }
}
