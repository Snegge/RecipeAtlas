using System.Data;
using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Common.Application;

namespace Recipoke.Api.Features.Cookbooks.Application;

public sealed class CookbookMedia(RecipeDb db, AccountAccess accounts, CookbookAccess access, CookbookOperations books)
{
    public async Task<StoredImage> GetAsync(Guid id, CancellationToken ct)
    {
        var actor = await accounts.RequireActiveAsync(ct);
        // A pending recipient may preview the invitation's cover, never recipe content.
        if (!await db.CookbookInvitations.AnyAsync(x => x.CookbookId == id && x.UserId == actor.Id, ct))
            await access.RequireAsync(id, CookbookPermission.Read, ct);
        return await db.CookbookImages.AsNoTracking().Where(x => x.CookbookId == id)
            .Select(x => new StoredImage(x.Bytes, x.ContentType)).SingleOrDefaultAsync(ct)
            ?? throw new OperationFailure(404, "Cookbook picture not found.");
    }

    public async Task<CookbookResponse> ReplaceAsync(Guid id, Stream body, long? length, VersionPrecondition expected, CancellationToken ct)
    {
        await access.RequireAsync(id, CookbookPermission.Manage, ct);
        expected.Require();
        var upload = await ImageUpload.ReadAsync(body, length, ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await access.RequireAsync(id, CookbookPermission.Manage, ct);
        var book = await db.Cookbooks.SingleAsync(x => x.Id == id, ct);
        ConcurrencyWrites.Advance(db.Entry(book).Property(x => x.Version), expected);
        var image = await db.CookbookImages.SingleOrDefaultAsync(x => x.CookbookId == id, ct);
        if (image is null)
        {
            image = new()
            {
                CookbookId = id
            };
            db.CookbookImages.Add(image);
        }

        image.Bytes = upload.Bytes;
        image.ContentType = upload.ContentType;
        image.Version = Guid.NewGuid();
        await ConcurrencyWrites.SaveAsync(db, expected, () => access.RequireAsync(id, CookbookPermission.Manage, ct), ct);
        var result = await books.GetAsync(id, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<CookbookResponse> RemoveAsync(Guid id, VersionPrecondition expected, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await access.RequireAsync(id, CookbookPermission.Manage, ct);
        var book = await db.Cookbooks.SingleAsync(x => x.Id == id, ct);
        ConcurrencyWrites.Advance(db.Entry(book).Property(x => x.Version), expected);
        var image = await db.CookbookImages.SingleOrDefaultAsync(x => x.CookbookId == id, ct);
        if (image is not null) db.CookbookImages.Remove(image);
        await ConcurrencyWrites.SaveAsync(db, expected, () => access.RequireAsync(id, CookbookPermission.Manage, ct), ct);
        var result = await books.GetAsync(id, ct);
        await tx.CommitAsync(ct);
        return result;
    }
}
