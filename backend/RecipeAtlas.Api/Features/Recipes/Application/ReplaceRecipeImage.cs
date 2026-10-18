using Recipoke.Api.Common.Application;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Features.Recipes.Mapping;

namespace Recipoke.Api.Features.Recipes.Application;

public sealed class ReplaceRecipeImage(RecipeDb db, CookbookAccess access)
{
    public const int MaxImageBytes = ImageUpload.MaxBytes;
    public async Task<ImageReplacementError?> ExecuteAsync(Guid cookbookId, Guid id, Stream body, long? contentLength, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await access.RequireAsync(cookbookId, CookbookPermission.Write, ct);
        if (contentLength > MaxImageBytes) return ImageReplacementError.TooLarge;
        var recipe = await db.Recipes.SingleOrDefaultAsync(x => x.Id == id && x.CookbookId == cookbookId, ct);
        if (recipe is null) return ImageReplacementError.NotFound;
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await body.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            if (output.Length + count > MaxImageBytes) return ImageReplacementError.TooLarge;
            output.Write(buffer, 0, count);
        }
        var bytes = output.ToArray();
        var type = ImageTypes.Detect(bytes);
        if (type is null) return ImageReplacementError.UnsupportedType;
        var image = await db.Images.SingleOrDefaultAsync(x => x.RecipeId == id, ct);
        if (image is null) { image = new RecipeImage { RecipeId = id }; db.Images.Add(image); }
        image.Bytes = bytes;
        image.ContentType = type;
        recipe.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return null;
    }
}
