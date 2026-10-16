using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Features.Recipes.Mapping;

namespace Recipoke.Api.Features.Recipes.Application;

public sealed class ReplaceRecipeImage(RecipeDb db)
{
    public const int MaxImageBytes = 5 * 1024 * 1024;
    public async Task<ImageReplacementError?> ExecuteAsync(Guid id, Stream body, long? contentLength, CancellationToken ct)
    {
        if (contentLength > MaxImageBytes) return ImageReplacementError.TooLarge;
        var recipe = await db.Recipes.SingleOrDefaultAsync(x => x.Id == id, ct);
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
        var type = DetectImageType(bytes);
        if (type is null) return ImageReplacementError.UnsupportedType;
        var image = await db.Images.SingleOrDefaultAsync(x => x.RecipeId == id, ct);
        if (image is null) { image = new RecipeImage { RecipeId = id }; db.Images.Add(image); }
        image.Bytes = bytes;
        image.ContentType = type;
        recipe.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return null;
    }
    // Signature screening preserves existing behaviour; it is not a full image decoder.
    private static string? DetectImageType(byte[] bytes)
    {
        var data = bytes.AsSpan();
        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return "image/png";
        if (data.Length >= 3 && data[0] == 255 && data[1] == 216 && data[2] == 255)
            return "image/jpeg";
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }
}
