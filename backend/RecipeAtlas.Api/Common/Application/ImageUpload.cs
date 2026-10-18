namespace Recipoke.Api.Common.Application;

public sealed record StoredImage(byte[] Bytes, string ContentType);
// Same bounded JPEG/PNG/WebP signature policy as recipe images; never accepts active SVG content.
public static class ImageUpload
{
    public const int MaxBytes = 5 * 1024 * 1024;

    public static async Task<StoredImage> ReadAsync(Stream body, long? length, CancellationToken ct)
    {
        if (length > MaxBytes)
            throw new OperationFailure(413, "Use a picture of 5 MiB or smaller.");
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await body.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + count > MaxBytes)
                throw new OperationFailure(413, "Use a picture of 5 MiB or smaller.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }

        var bytes = output.ToArray();
        var type = ImageTypes.Detect(bytes) ?? throw new OperationFailure(415, "Choose a JPEG, PNG or WebP picture.");
        return new(bytes, type);
    }
}
