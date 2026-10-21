using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Recipoke.Api.Features.Cookbooks.Application;

public sealed class CookbookOperations(RecipeDb db, AccountAccess accounts, CookbookAccess access, CookbookInvitations invitations)
{
    public async Task<IReadOnlyList<CookbookResponse>> ListAsync(CancellationToken ct)
    {
        var user = await accounts.RequireActiveAsync(ct);
        return await db.Cookbooks.AsNoTracking()
            .Where(x => x.OwnerUserId == user.Id || db.Memberships.Any(m => m.CookbookId == x.Id && m.UserId == user.Id))
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new CookbookResponse(x.Id, x.Name,
                x.OwnerUserId == user.Id ? "Owner" : db.Memberships.Where(m => m.CookbookId == x.Id && m.UserId == user.Id)
                    .Select(m => m.Role == CookbookRole.Editor ? "Editor" : "Viewer").Single(),
                x.Version, x.Description, x.Image == null ? null : "/api/cookbooks/" + x.Id + "/image?v=" + x.Image.Version,
                db.Recipes.Count(r => r.CookbookId == x.Id)))
            .ToListAsync(ct);
    }

    public async Task<CookbookResponse> GetAsync(Guid id, CancellationToken ct)
    {
        await using var snapshot = await ReadSnapshot.BeginAsync(db, ct);
        var role = await access.RequireAsync(id, CookbookPermission.Read, ct);
        return await db.Cookbooks.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new CookbookResponse(x.Id, x.Name, role.ToString(), x.Version, x.Description,
                x.Image == null ? null : "/api/cookbooks/" + x.Id + "/image?v=" + x.Image.Version,
                db.Recipes.Count(r => r.CookbookId == x.Id)))
            .SingleAsync(ct);
    }

    public async Task<CookbookResponse> CreateAsync(CreateCookbookInput input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var user = await accounts.RequireActiveAsync(ct);
        if (input.Members?.Count > 100) throw new OperationFailure(400, "Invite at most 100 members at a time.");
        if (input.Members?.Any(member => member is null) == true)
            throw new OperationFailure(400, "Each invitation requires a username and role.");
        var book = new Cookbook
        {
            Name = CookbookValidation.Name(input.Name), Description = CookbookValidation.Description(input.Description), OwnerUserId = user.Id
        };
        db.Cookbooks.Add(book);
        foreach (var member in input.Members ?? []) await invitations.StageAsync(book, member, ct);
        await db.SaveChangesAsync(ct);
        var result = new CookbookResponse(book.Id, book.Name, "Owner", book.Version, book.Description);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<CookbookResponse> RenameAsync(Guid id, CookbookNameInput input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await access.RequireAsync(id, CookbookPermission.Manage, ct);
        var book = await db.Cookbooks.SingleAsync(x => x.Id == id, ct);
        var expected = new VersionPrecondition(input.ExpectedVersion);
        ConcurrencyWrites.Advance(db.Entry(book).Property(x => x.Version), expected);
        book.Name = CookbookValidation.Name(input.Name);
        book.Description = CookbookValidation.Description(input.Description);
        await ConcurrencyWrites.SaveAsync(db, expected, () => access.RequireAsync(id, CookbookPermission.Manage, ct), ct);
        var result = await GetAsync(id, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task DeleteAsync(Guid id, VersionPrecondition expected, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await access.RequireAsync(id, CookbookPermission.Manage, ct);
        var book = await db.Cookbooks.SingleAsync(x => x.Id == id, ct);
        ConcurrencyWrites.Advance(db.Entry(book).Property(x => x.Version), expected);
        // The final versioned DELETE and all preceding child deletes belong to this one transaction.
        await db.Recipes.Where(x => x.CookbookId == id).ExecuteDeleteAsync(ct);
        await db.Memberships.Where(x => x.CookbookId == id).ExecuteDeleteAsync(ct);
        db.Cookbooks.Remove(book);
        await ConcurrencyWrites.SaveAsync(db, expected, () => access.RequireAsync(id, CookbookPermission.Manage, ct), ct);
        await tx.CommitAsync(ct);
    }

    public async Task<CookbookMembersResponse> MembersAsync(Guid id, CancellationToken ct)
    {
        await using var snapshot = await ReadSnapshot.BeginAsync(db, ct);
        await access.RequireAsync(id, CookbookPermission.Manage, ct);
        var book = await GetAsync(id, ct);
        var owner = await db.Cookbooks.Where(x => x.Id == id).Select(x => x.OwnerUserId).SingleAsync(ct);
        var members = await db.Users.AsNoTracking()
            .Where(x => x.Id == owner || db.Memberships.Any(m => m.CookbookId == id && m.UserId == x.Id))
            .OrderBy(x => x.NormalizedUserName)
            .Select(x => new MemberResponse(x.Id, x.UserName!, x.DisplayName,
                x.Id == owner ? "Owner" : db.Memberships.Where(m => m.CookbookId == id && m.UserId == x.Id)
                    .Select(m => m.Role == CookbookRole.Editor ? "Editor" : "Viewer").Single(), x.IsActive,
                x.ProfileImage == null ? null : "/api/accounts/" + x.Id + "/avatar?v=" + x.ProfileImage.Version, false))
            .ToListAsync(ct);
        var pending = await db.CookbookInvitations.AsNoTracking().Where(x => x.CookbookId == id)
            .Join(db.Users, i => i.UserId, u => u.Id, (i, u) => new MemberResponse(u.Id, u.UserName!, u.DisplayName,
                i.Role == CookbookRole.Editor ? "Editor" : "Viewer", u.IsActive,
                u.ProfileImage == null ? null : "/api/accounts/" + u.Id + "/avatar?v=" + u.ProfileImage.Version, true))
            .ToListAsync(ct);
        return new(book, members.Concat(pending).ToArray());
    }

    public Task<VersionResponse> AddMemberAsync(Guid id, AddMemberInput input, CancellationToken ct) => invitations.InviteAsync(id, input, ct);

    public async Task<VersionResponse?> ChangeMemberAsync(Guid id, Guid userId, string? role, bool remove, VersionPrecondition expected, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var actor = await accounts.RequireActiveAsync(ct);
        var permission = remove && actor.Id == userId ? CookbookPermission.Read : CookbookPermission.Manage;
        await access.RequireAsync(id, permission, ct);
        var book = await db.Cookbooks.SingleAsync(x => x.Id == id, ct);
        if (book.OwnerUserId == userId) throw new OperationFailure(403, "Ownership cannot be removed or transferred.");
        ConcurrencyWrites.Advance(db.Entry(book).Property(x => x.Version), expected);
        var member = await db.Memberships.SingleOrDefaultAsync(x => x.CookbookId == id && x.UserId == userId, ct)
            ?? throw new OperationFailure(404, "Member not found.", "resource_unavailable");
        if (remove) db.Memberships.Remove(member);
        else member.Role = CookbookValidation.MemberRole(role);
        await ConcurrencyWrites.SaveAsync(db, expected, () => access.RequireAsync(id, permission, ct), ct);
        var result = remove && actor.Id == userId ? null : new VersionResponse(book.Version);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task LeaveAsync(Guid id, VersionPrecondition expected, CancellationToken ct)
    {
        var actor = await accounts.RequireActiveAsync(ct);
        await ChangeMemberAsync(id, actor.Id, null, true, expected, ct);
    }
}
