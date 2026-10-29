using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Common.Application;

namespace Recipoke.Api.Features.Accounts.Application;

public sealed class AccountProfiles(RecipeDb db, AccountAccess access, UserManager<AppUser> users, ILookupNormalizer normalizer)
{
    public async Task<CurrentUserResponse> CurrentAsync(CancellationToken ct)
    {
        var user = await access.RequireActiveAsync(ct);
        return await ResponseAsync(user.Id, ct);
    }

    private Task<CurrentUserResponse> ResponseAsync(Guid id, CancellationToken ct) =>
        db.Users.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new CurrentUserResponse(
                x.Id, x.UserName!, x.DisplayName, x.IsAdministrator, x.Email,
                x.ProfileImage == null ? null : "/api/accounts/" + x.Id + "/avatar?v=" + x.ProfileImage.Version))
            .SingleAsync(ct);

    public async Task<CurrentUserResponse> UpdateAsync(UpdateProfileInput input, CancellationToken ct)
    {
        await using var tx = await WriteTransaction.BeginAsync(db, ct);
        var active = await access.RequireActiveAsync(ct);
        var profile = AccountValidation.Profile(active.UserName, input.DisplayName);
        var email = AccountValidation.ContactEmail(input.Email);
        await AccountValidation.CheckEmailAvailableAsync(db, normalizer, email, active.Id, ct);
        var user = (await users.FindByIdAsync(active.Id.ToString()))!;
        await db.Entry(user).ReloadAsync(ct);
        user.DisplayName = profile.DisplayName;
        if (user.Email != email)
            user.EmailConfirmed = false;
        user.Email = email;
        AccountValidation.RequireSuccess(await users.UpdateAsync(user));
        await tx.CommitAsync(ct);
        return await ResponseAsync(active.Id, ct);
    }

    public async Task<CurrentUserResponse> ReplaceImageAsync(Stream body, long? length, CancellationToken ct)
    {
        await access.RequireActiveAsync(ct);
        var uploaded = await ImageUpload.ReadAsync(body, length, ct);
        await using var tx = await WriteTransaction.BeginAsync(db, ct);
        var user = await access.RequireActiveAsync(ct);
        var image = await db.ProfileImages.SingleOrDefaultAsync(x => x.UserId == user.Id, ct);
        if (image is null)
        {
            image = new()
            {
                UserId = user.Id
            };
            db.ProfileImages.Add(image);
        }

        image.Bytes = uploaded.Bytes;
        image.ContentType = uploaded.ContentType;
        image.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ResponseAsync(user.Id, ct);
    }

    public async Task RemoveImageAsync(CancellationToken ct)
    {
        await using var tx = await WriteTransaction.BeginAsync(db, ct);
        var user = await access.RequireActiveAsync(ct);
        await db.ProfileImages.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<StoredImage> ImageAsync(Guid userId, CancellationToken ct)
    {
        var actor = await access.RequireActiveAsync(ct);
        var visible = actor.Id == userId || actor.IsAdministrator ||
            await db.Cookbooks.AnyAsync(book =>
                (book.OwnerUserId == actor.Id || db.Memberships.Any(m => m.CookbookId == book.Id && m.UserId == actor.Id)) &&
                (book.OwnerUserId == userId || db.Memberships.Any(m => m.CookbookId == book.Id && m.UserId == userId)), ct) ||
            await db.CookbookInvitations.AnyAsync(i =>
                i.UserId == actor.Id && i.Cookbook.OwnerUserId == userId ||
                i.UserId == userId && i.Cookbook.OwnerUserId == actor.Id, ct);
        if (!visible)
            throw new OperationFailure(404, "Profile picture not found.");
        return await db.ProfileImages.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new StoredImage(x.Bytes, x.ContentType)).SingleOrDefaultAsync(ct)
            ?? throw new OperationFailure(404, "Profile picture not found.");
    }
}
