using System.Data;
using Microsoft.EntityFrameworkCore;
using Recipoke.Domain.Shopping;

namespace Recipoke.Api.Features.Shopping.Application;

public sealed partial class ShoppingOperations
{
    public async Task<ShoppingMutationResponse> CreateAsync(CreateShoppingListInput input, CancellationToken ct = default)
    {
        await using var tx = await WriteTransaction.BeginAsync(db, ct);
        var actor = await accounts.RequireActiveAsync(ct);
        RequireId(input.ListId, "listId"); RequireId(input.OperationId, "operationId");
        if (await db.ShoppingListTombstones.AnyAsync(x => x.Id == input.ListId, ct)) throw ShoppingAccess.Unavailable();
        if (await db.ShoppingLists.AnyAsync(x => x.Id == input.ListId, ct))
        {
            await access.RequireAsync(input.ListId, false, ct);
            return await ReplayAsync(input.ListId, actor.Id, input.OperationId, "create", input, ct)
                ?? throw new OperationFailure(409, "This list ID has already been used.", "shopping_id_reused");
        }
        var list = new ShoppingList { Id = input.ListId, OwnerUserId = actor.Id, Name = Text(input.Name, 100, "List name") };
        db.ShoppingLists.Add(list);
        var result = Administrative(list);
        Receipt(list.Id, actor.Id, input.OperationId, "create", input, result);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await NotifyAsync(list.Id);
        return result;
    }

    public Task<ShoppingMutationResponse> RenameAsync(Guid listId, RenameShoppingListInput input, CancellationToken ct = default) =>
        WriteAsync(listId, input.OperationId, "rename", input, true, new(input.ExpectedVersion), (list, _) =>
        {
            list.Name = Text(input.Name, 100, "List name");
            Advance(list, new(input.ExpectedVersion));
            return Task.FromResult(Administrative(list));
        }, ct);

    public async Task DeleteAsync(Guid listId, VersionPrecondition expected, Guid operationId, CancellationToken ct = default)
    {
        await using var tx = await WriteTransaction.BeginAsync(db, ct);
        await access.RequireAsync(listId, true, ct);
        RequireId(operationId, "operationId");
        var actor = await accounts.RequireActiveAsync(ct);
        // An ID already used for another successful mutation cannot be repurposed for deletion.
        await ReplayAsync(listId, actor.Id, operationId, "delete-list", new { expected.Value, expected.IsEntityTag }, ct);
        var list = await db.ShoppingLists.SingleAsync(x => x.Id == listId, ct);
        Advance(list, expected);
        db.ShoppingLists.Remove(list);
        db.ShoppingListTombstones.Add(new() { Id = listId });
        await ConcurrencyWrites.SaveAsync(db, expected, () => access.RequireAsync(listId, true, ct), ct);
        await tx.CommitAsync(ct);
        await NotifyAsync(listId);
    }

    public Task<ShoppingMutationResponse> InviteAsync(Guid listId, InviteShoppingMemberInput input, CancellationToken ct = default) =>
        WriteAsync(listId, input.OperationId, "invite", input, true, new(input.ExpectedVersion), async (list, _) =>
        {
            var username = normalizer.NormalizeName(input.Username?.Trim());
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedUserName == username && x.IsActive, ct)
                ?? throw new OperationFailure(404, "Active account not found. Enter its exact username.", "resource_unavailable");
            if (user.Id == list.OwnerUserId || await db.ShoppingMemberships.AnyAsync(x => x.ListId == listId && x.UserId == user.Id, ct))
                throw new OperationFailure(409, "This account already belongs to the shopping list.", "shopping_member_exists");
            if (await db.ShoppingInvitations.AnyAsync(x => x.ListId == listId && x.UserId == user.Id, ct))
                throw new OperationFailure(409, "This account already has a pending invitation.", "shopping_invitation_exists");
            db.ShoppingInvitations.Add(new() { ListId = listId, UserId = user.Id });
            Advance(list, new(input.ExpectedVersion));
            return Administrative(list);
        }, ct);

    public Task<ShoppingMutationResponse> CancelInvitationAsync(Guid listId, Guid invitationId, VersionPrecondition expected, Guid operationId, CancellationToken ct = default) =>
        WriteAsync(listId, operationId, "cancel-invitation", new { invitationId, expected.Value, expected.IsEntityTag }, true, expected, async (list, _) =>
        {
            var invitation = await db.ShoppingInvitations.SingleOrDefaultAsync(x => x.Id == invitationId && x.ListId == listId, ct) ?? throw ShoppingAccess.Unavailable();
            db.ShoppingInvitations.Remove(invitation);
            Advance(list, expected);
            return Administrative(list);
        }, ct);

    public async Task<ShoppingMutationResponse> RespondAsync(Guid listId, Guid invitationId, RespondShoppingInvitationInput input, CancellationToken ct = default)
    {
        await using var tx = await WriteTransaction.BeginAsync(db, ct);
        var actor = await accounts.RequireActiveAsync(ct);
        var invitation = await db.ShoppingInvitations.SingleOrDefaultAsync(x => x.Id == invitationId && x.ListId == listId && x.UserId == actor.Id, ct);
        if (invitation is null) await access.RequireAsync(listId, false, ct);
        var payload = new { invitationId, input };
        var replay = await ReplayAsync(listId, actor.Id, input.OperationId, "respond", payload, ct);
        if (replay is not null) return replay;
        if (invitation is null) throw ShoppingAccess.Unavailable();
        var list = await db.ShoppingLists.SingleAsync(x => x.Id == listId, ct);
        var expected = new VersionPrecondition(input.ExpectedVersion);
        Advance(list, expected);
        if (input.Accept) db.ShoppingMemberships.Add(new() { ListId = listId, UserId = actor.Id });
        db.ShoppingInvitations.Remove(invitation);
        var result = input.Accept ? Administrative(list) : new ShoppingMutationResponse(listId, null, null, null);
        Receipt(listId, actor.Id, input.OperationId, "respond", payload, result);
        await ConcurrencyWrites.SaveAsync(db, expected, async () =>
        {
            await accounts.RequireActiveAsync(ct);
            if (!await db.ShoppingInvitations.AnyAsync(x => x.Id == invitationId && x.ListId == listId && x.UserId == actor.Id, ct))
                throw ShoppingAccess.Unavailable();
        }, ct);
        await tx.CommitAsync(ct);
        await NotifyAsync(listId);
        return result;
    }

    public Task<ShoppingMutationResponse> RemoveMemberAsync(Guid listId, Guid userId, VersionPrecondition expected, Guid operationId, CancellationToken ct = default) =>
        WriteAsync(listId, operationId, "remove-member", new { userId, expected.Value, expected.IsEntityTag }, true, expected,
            async (list, actor) => await RemoveMembershipAsync(list, userId, actor, expected, ct), ct, permittedSelfRemoval: userId);

    public Task<ShoppingMutationResponse> LeaveAsync(Guid listId, ShoppingVersionInput input, CancellationToken ct = default) =>
        WriteAsync(listId, input.OperationId, "leave", input, false, new(input.ExpectedVersion),
            async (list, actor) => await RemoveMembershipAsync(list, actor, actor, new(input.ExpectedVersion), ct), ct);

    private async Task<ShoppingMutationResponse> RemoveMembershipAsync(ShoppingList list, Guid userId, Guid actor, VersionPrecondition expected, CancellationToken ct)
    {
        if (actor != userId) await access.RequireAsync(list.Id, true, ct);
        if (list.OwnerUserId == userId) throw new OperationFailure(403, "Transfer ownership before leaving the list.", "shopping_owner_required");
        var member = await db.ShoppingMemberships.SingleOrDefaultAsync(x => x.ListId == list.Id && x.UserId == userId, ct) ?? throw ShoppingAccess.Unavailable();
        db.ShoppingMemberships.Remove(member);
        await db.ShoppingReservations.Where(x => x.ListId == list.Id && x.UserId == userId).ExecuteDeleteAsync(ct);
        Advance(list, expected);
        return actor == userId ? new(list.Id, null, null, null) : Administrative(list);
    }

    public Task<ShoppingMutationResponse> TransferAsync(Guid listId, TransferShoppingListInput input, CancellationToken ct = default) =>
        WriteAsync(listId, input.OperationId, "transfer", input, true, new(input.ExpectedVersion), async (list, actor) =>
        {
            var member = await db.ShoppingMemberships.SingleOrDefaultAsync(x => x.ListId == listId && x.UserId == input.NewOwnerUserId, ct);
            if (member is null || !await db.Users.AnyAsync(x => x.Id == input.NewOwnerUserId && x.IsActive, ct)) throw ShoppingAccess.Unavailable();
            db.ShoppingMemberships.Remove(member);
            db.ShoppingMemberships.Add(new() { ListId = listId, UserId = actor });
            list.OwnerUserId = input.NewOwnerUserId;
            Advance(list, new(input.ExpectedVersion));
            return Administrative(list);
        }, ct);
}
