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
            .Select(x => new CookbookResponse(
                x.Id, x.Name,
                x.OwnerUserId == user.Id ? "Owner" : db.Memberships
                    .Where(m => m.CookbookId == x.Id && m.UserId == user.Id)
                    .Select(m => m.Role == CookbookRole.Editor ? "Editor" : "Viewer").Single(),
                x.Description,
                x.Image == null ? null : "/api/cookbooks/" + x.Id + "/image?v=" + x.Image.Version,
                db.Recipes.Count(r => r.CookbookId == x.Id)))
            .ToListAsync(ct);
    }

    public async Task<CookbookResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var role = await access.RequireAsync(id, CookbookPermission.Read, ct);
        return await db.Cookbooks.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new CookbookResponse(
                x.Id, x.Name, role.ToString(), x.Description,
                x.Image == null ? null : "/api/cookbooks/" + x.Id + "/image?v=" + x.Image.Version,
                db.Recipes.Count(r => r.CookbookId == x.Id)))
            .SingleAsync(ct);
    }

    public async Task<CookbookResponse> CreateAsync(CreateCookbookInput input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var user = await accounts.RequireActiveAsync(ct);
        if (input.Members?.Count > 100)
            throw new OperationFailure(400, "Invite at most 100 members at a time.");
        if (input.Members?.Any(member => member is null) == true)
            throw new OperationFailure(400, "Each invitation requires a username and role.");
        var book = new Cookbook
        {
            Name = CookbookValidation.Name(input.Name),
            Description = CookbookValidation.Description(input.Description),
            OwnerUserId = user.Id
        };
        db.Cookbooks.Add(book);
        foreach (var member in input.Members ?? [])
            await invitations.StageAsync(book, member, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(book.Id, book.Name, "Owner", book.Description);
    }

    public async Task RenameAsync(Guid id, CookbookNameInput input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await access.RequireAsync(id, CookbookPermission.Manage, ct);
        var book = await db.Cookbooks.SingleAsync(x => x.Id == id, ct);
        book.Name = CookbookValidation.Name(input.Name);
        book.Description = CookbookValidation.Description(input.Description);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await access.RequireAsync(id, CookbookPermission.Manage, ct);
        // One transaction: recipe cascades remove ingredients/steps/images, then restricted memberships.
        await db.Recipes.Where(x => x.CookbookId == id).ExecuteDeleteAsync(ct);
        await db.Memberships.Where(x => x.CookbookId == id).ExecuteDeleteAsync(ct);
        await db.Cookbooks.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<MemberResponse>> MembersAsync(Guid id, CancellationToken ct)
    {
        await access.RequireAsync(id, CookbookPermission.Manage, ct);
        var owner = await db.Cookbooks.Where(x => x.Id == id).Select(x => x.OwnerUserId).SingleAsync(ct);
        var members = await db.Users.AsNoTracking()
            .Where(x => x.Id == owner || db.Memberships.Any(m => m.CookbookId == id && m.UserId == x.Id))
            .OrderBy(x => x.NormalizedUserName)
            .Select(x => new MemberResponse(
                x.Id, x.UserName!, x.DisplayName,
                x.Id == owner ? "Owner" : db.Memberships.Where(m => m.CookbookId == id && m.UserId == x.Id)
                    .Select(m => m.Role == CookbookRole.Editor ? "Editor" : "Viewer").Single(),
                x.IsActive,
                x.ProfileImage == null ? null : "/api/accounts/" + x.Id + "/avatar?v=" + x.ProfileImage.Version,
                false))
            .ToListAsync(ct);
        var pending = await db.CookbookInvitations.AsNoTracking().Where(x => x.CookbookId == id)
            .Join(db.Users, i => i.UserId, u => u.Id, (i, u) => new MemberResponse(
                u.Id, u.UserName!, u.DisplayName, i.Role == CookbookRole.Editor ? "Editor" : "Viewer", u.IsActive,
                u.ProfileImage == null ? null : "/api/accounts/" + u.Id + "/avatar?v=" + u.ProfileImage.Version,
                true))
            .ToListAsync(ct);
        return members.Concat(pending).ToArray();
    }

    public Task AddMemberAsync(Guid id, AddMemberInput input, CancellationToken ct) => invitations.InviteAsync(id, input, ct);

    public async Task ChangeMemberAsync(Guid id, Guid userId, string? role, bool remove, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var actor = await accounts.RequireActiveAsync(ct);
        await access.RequireAsync(id, remove && actor.Id == userId ? CookbookPermission.Read : CookbookPermission.Manage, ct);
        if (await db.Cookbooks.AnyAsync(x => x.Id == id && x.OwnerUserId == userId, ct))
            throw new OperationFailure(403, "Ownership cannot be removed or transferred.");
        var member = await db.Memberships.SingleOrDefaultAsync(x => x.CookbookId == id && x.UserId == userId, ct)
            ?? throw new OperationFailure(404, "Member not found.");
        if (remove)
            db.Memberships.Remove(member);
        else
            member.Role = CookbookValidation.MemberRole(role);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task LeaveAsync(Guid id, CancellationToken ct)
    {
        var actor = await accounts.RequireActiveAsync(ct);
        await ChangeMemberAsync(id, actor.Id, null, true, ct);
    }
}
