using System.Text.Json.Serialization;
using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace Recipoke.Api.Features.Cookbooks.Application;

public sealed record CookbookResponse(Guid Id, string Name, string Role);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CookbookNameInput(string? Name);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AddMemberInput(string? Username, string? Role);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MemberRoleInput(string? Role);
public sealed record MemberResponse(Guid UserId, string Username, string DisplayName, string Role, bool IsActive);
public sealed class CookbookOperations(RecipeDb db, AccountAccess accounts, CookbookAccess access, ILookupNormalizer normalizer)
{
    private static string Name(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100) throw new OperationFailure(400, "Use a cookbook name with 1–100 characters.");
        return name;
    }
    private static CookbookRole MemberRole(string? role) => role switch
    {
        "Editor" => CookbookRole.Editor, "Viewer" => CookbookRole.Viewer,
        _ => throw new OperationFailure(400, "Choose Editor or Viewer.")
    };
    public async Task<IReadOnlyList<CookbookResponse>> ListAsync(CancellationToken ct)
    {
        var user = await accounts.RequireActiveAsync(ct);
        return await db.Cookbooks.AsNoTracking().Where(x => x.OwnerUserId == user.Id || db.Memberships.Any(m => m.CookbookId == x.Id && m.UserId == user.Id))
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new CookbookResponse(x.Id, x.Name,
                x.OwnerUserId == user.Id ? "Owner" : db.Memberships.Where(m => m.CookbookId == x.Id && m.UserId == user.Id).Select(m => m.Role == CookbookRole.Editor ? "Editor" : "Viewer").Single())).ToListAsync(ct);
    }
    public async Task<CookbookResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var role = await access.RequireAsync(id, CookbookPermission.Read, ct);
        var book = await db.Cookbooks.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        return new(id, book.Name, role.ToString());
    }
    public async Task<CookbookResponse> CreateAsync(CookbookNameInput input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var user = await accounts.RequireActiveAsync(ct);
        var book = new Cookbook { Name = Name(input.Name), OwnerUserId = user.Id };
        db.Cookbooks.Add(book); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(book.Id, book.Name, "Owner");
    }
    public async Task RenameAsync(Guid id, CookbookNameInput input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await access.RequireAsync(id, CookbookPermission.Manage, ct);
        var book = await db.Cookbooks.SingleAsync(x => x.Id == id, ct); book.Name = Name(input.Name);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task<IReadOnlyList<MemberResponse>> MembersAsync(Guid id, CancellationToken ct)
    {
        await access.RequireAsync(id, CookbookPermission.Manage, ct);
        var owner = await db.Cookbooks.Where(x => x.Id == id).Select(x => x.OwnerUserId).SingleAsync(ct);
        var users = await db.Users.AsNoTracking().Where(x => x.Id == owner || db.Memberships.Any(m => m.CookbookId == id && m.UserId == x.Id))
            .OrderBy(x => x.NormalizedUserName).Select(x => new MemberResponse(x.Id, x.UserName!, x.DisplayName,
                x.Id == owner ? "Owner" : db.Memberships.Where(m => m.CookbookId == id && m.UserId == x.Id).Select(m => m.Role == CookbookRole.Editor ? "Editor" : "Viewer").Single(), x.IsActive)).ToListAsync(ct);
        return users;
    }
    public async Task AddMemberAsync(Guid id, AddMemberInput input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await access.RequireAsync(id, CookbookPermission.Manage, ct);
        var role = MemberRole(input.Role);
        var normalized = normalizer.NormalizeName(input.Username?.Trim());
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedUserName == normalized && x.IsActive, ct)
            ?? throw new OperationFailure(404, "Active account not found. Enter its exact username.");
        if (await db.Cookbooks.AnyAsync(x => x.Id == id && x.OwnerUserId == user.Id, ct) || await db.Memberships.AnyAsync(x => x.CookbookId == id && x.UserId == user.Id, ct))
            throw new OperationFailure(409, "This account already belongs to the cookbook.");
        db.Memberships.Add(new() { CookbookId = id, UserId = user.Id, Role = role });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task ChangeMemberAsync(Guid id, Guid userId, string? role, bool remove, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var actor = await accounts.RequireActiveAsync(ct);
        // Non-owners may remove only themselves, after proving that they can see the cookbook.
        await access.RequireAsync(id, remove && actor.Id == userId ? CookbookPermission.Read : CookbookPermission.Manage, ct);
        if (await db.Cookbooks.AnyAsync(x => x.Id == id && x.OwnerUserId == userId, ct))
            throw new OperationFailure(403, "Ownership cannot be removed or transferred.");
        var member = await db.Memberships.SingleOrDefaultAsync(x => x.CookbookId == id && x.UserId == userId, ct) ?? throw new OperationFailure(404, "Member not found.");
        if (remove) db.Memberships.Remove(member); else member.Role = MemberRole(role);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task LeaveAsync(Guid id, CancellationToken ct)
    {
        var actor = await accounts.RequireActiveAsync(ct);
        await ChangeMemberAsync(id, actor.Id, null, true, ct);
    }
}
