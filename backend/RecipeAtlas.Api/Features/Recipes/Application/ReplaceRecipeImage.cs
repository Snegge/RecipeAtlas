using Recipoke.Api.Common.Application;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Features.Recipes.Mapping;

namespace Recipoke.Api.Features.Recipes.Application;

public sealed class ReplaceRecipeImage(RecipeDb db, CookbookAccess access)
{
    public const int MaxImageBytes = ImageUpload.MaxBytes;
    public async Task<ImageReplacementResult> ExecuteAsync(Guid cookbookId, Guid id, Stream body, long? contentLength, VersionPrecondition expected, CancellationToken ct)
    {
        await using var tx = await WriteTransaction.BeginAsync(db, ct);
        await access.RequireAsync(cookbookId, CookbookPermission.Write, ct);
        expected.Require();
        if (contentLength > MaxImageBytes) return new(ImageReplacementError.TooLarge, null);
        var recipe = await db.Recipes.SingleOrDefaultAsync(x => x.Id == id && x.CookbookId == cookbookId, ct);
        if (recipe is null) throw new OperationFailure(404, "This recipe is no longer available.", "resource_unavailable");
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await body.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            if (output.Length + count > MaxImageBytes) return new(ImageReplacementError.TooLarge, null);
            output.Write(buffer, 0, count);
        }
        var bytes = output.ToArray();
        var type = ImageTypes.Detect(bytes);
        if (type is null) return new(ImageReplacementError.UnsupportedType, null);
        ConcurrencyWrites.Advance(db.Entry(recipe).Property(x => x.Version), expected);
        var image = await db.Images.SingleOrDefaultAsync(x => x.RecipeId == id, ct);
        if (image is null) { image = new RecipeImage { RecipeId = id }; db.Images.Add(image); }
        image.Bytes = bytes;
        image.ContentType = type;
        recipe.UpdatedAtUtc = DateTime.UtcNow;
        await ConcurrencyWrites.SaveAsync(db, expected, () => RecipeAvailability.RequireWriteAsync(db, access, cookbookId, id, ct), ct);
        await tx.CommitAsync(ct);
        return new(null, new ImageResponse($"/api/cookbooks/{cookbookId}/recipes/{id}/image", recipe.Version, recipe.UpdatedAtUtc));
    }
}
