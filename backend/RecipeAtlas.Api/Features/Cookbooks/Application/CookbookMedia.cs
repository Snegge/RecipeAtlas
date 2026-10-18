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

    public async Task<CookbookResponse> ReplaceAsync(Guid id, Stream body, long? length, CancellationToken ct)
    {
        await access.RequireAsync(id, CookbookPermission.Manage, ct);
        var upload = await ImageUpload.ReadAsync(body, length, ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await access.RequireAsync(id, CookbookPermission.Manage, ct);
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
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await books.GetAsync(id, ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await access.RequireAsync(id, CookbookPermission.Manage, ct);
        await db.CookbookImages.Where(x => x.CookbookId == id).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
    }
}
