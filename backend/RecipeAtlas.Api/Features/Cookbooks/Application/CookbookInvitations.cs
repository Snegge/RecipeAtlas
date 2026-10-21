using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Common.Application;

namespace Recipoke.Api.Features.Cookbooks.Application;

public sealed class CookbookInvitations(RecipeDb db, AccountAccess accounts, CookbookAccess access, ILookupNormalizer normalizer, TimeProvider clock)
{
    // Called inside the transaction owned by cookbook creation or InviteAsync. Never commits independently.
    public async Task StageAsync(Cookbook book, AddMemberInput input, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Invitation staging requires the caller's transaction.");
        var role = CookbookValidation.MemberRole(input.Role);
        var normalized = normalizer.NormalizeName(input.Username?.Trim());
        var user = await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.NormalizedUserName == normalized && x.IsActive, ct)
            ?? throw new OperationFailure(404, "Active account not found. Enter its exact username.");
        if (user.Id == book.OwnerUserId || await db.Memberships.AnyAsync(x => x.CookbookId == book.Id && x.UserId == user.Id, ct))
            throw new OperationFailure(409, "This account already belongs to the cookbook.");
        if (db.CookbookInvitations.Local.Any(x => x.CookbookId == book.Id && x.UserId == user.Id) ||
            await db.CookbookInvitations.AnyAsync(x => x.CookbookId == book.Id && x.UserId == user.Id, ct))
            throw new OperationFailure(409, "This account already has a pending invitation to the cookbook.");
        db.CookbookInvitations.Add(new() { CookbookId = book.Id, UserId = user.Id, Role = role, CreatedAtUtc = clock.GetUtcNow() });
    }

    public async Task<VersionResponse> InviteAsync(Guid id, AddMemberInput input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await access.RequireAsync(id, CookbookPermission.Manage, ct);
        var book = await db.Cookbooks.SingleAsync(x => x.Id == id, ct);
        var expected = new VersionPrecondition(input.ExpectedVersion);
        ConcurrencyWrites.Advance(db.Entry(book).Property(x => x.Version), expected);
        await StageAsync(book, input, ct);
        await ConcurrencyWrites.SaveAsync(db, expected, () => access.RequireAsync(id, CookbookPermission.Manage, ct), ct);
        await tx.CommitAsync(ct);
        return new(book.Version);
    }

    public async Task<IReadOnlyList<CookbookInvitationResponse>> InboxAsync(CancellationToken ct)
    {
        var user = await accounts.RequireActiveAsync(ct);
        return await db.CookbookInvitations.AsNoTracking().Where(x => x.UserId == user.Id)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Take(5)
            .Select(x => new CookbookInvitationResponse(
                x.Id, x.CookbookId, x.Cookbook.Name, x.Role.ToString(),
                db.Users.Where(u => u.Id == x.Cookbook.OwnerUserId).Select(u => u.DisplayName).Single(),
                db.Users.Where(u => u.Id == x.Cookbook.OwnerUserId)
                    .Select(u => u.ProfileImage == null ? null : "/api/accounts/" + u.Id + "/avatar?v=" + u.ProfileImage.Version).Single(),
                x.Cookbook.Image == null ? null : "/api/cookbooks/" + x.CookbookId + "/image?v=" + x.Cookbook.Image.Version, x.Cookbook.Version))
            .ToListAsync(ct);
    }

    public async Task RespondAsync(Guid id, bool accept, VersionPrecondition expected, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var user = await accounts.RequireActiveAsync(ct);
        var invitation = await db.CookbookInvitations.SingleOrDefaultAsync(x => x.Id == id && x.UserId == user.Id, ct)
            ?? throw new OperationFailure(404, "Cookbook invitation not found.", "resource_unavailable");
        var book = await db.Cookbooks.SingleAsync(x => x.Id == invitation.CookbookId, ct);
        ConcurrencyWrites.Advance(db.Entry(book).Property(x => x.Version), expected);
        if (accept)
        {
            if (!await db.Memberships.AnyAsync(x => x.CookbookId == invitation.CookbookId && x.UserId == user.Id, ct))
                db.Memberships.Add(new() { CookbookId = invitation.CookbookId, UserId = user.Id, Role = invitation.Role });
        }

        db.CookbookInvitations.Remove(invitation);
        await ConcurrencyWrites.SaveAsync(db, expected, async () =>
        {
            await accounts.RequireActiveAsync(ct);
            if (!await db.CookbookInvitations.AnyAsync(x => x.Id == id && x.UserId == user.Id, ct))
                throw new OperationFailure(404, "Cookbook invitation is no longer available.", "resource_unavailable");
        }, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<VersionResponse> CancelAsync(Guid cookbookId, Guid userId, VersionPrecondition expected, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await access.RequireAsync(cookbookId, CookbookPermission.Manage, ct);
        var book = await db.Cookbooks.SingleAsync(x => x.Id == cookbookId, ct);
        ConcurrencyWrites.Advance(db.Entry(book).Property(x => x.Version), expected);
        var pending = await db.CookbookInvitations.SingleOrDefaultAsync(x => x.CookbookId == cookbookId && x.UserId == userId, ct)
            ?? throw new OperationFailure(404, "Cookbook invitation not found.", "resource_unavailable");
        db.CookbookInvitations.Remove(pending);
        await ConcurrencyWrites.SaveAsync(db, expected, () => access.RequireAsync(cookbookId, CookbookPermission.Manage, ct), ct);
        await tx.CommitAsync(ct);
        return new(book.Version);
    }
}
