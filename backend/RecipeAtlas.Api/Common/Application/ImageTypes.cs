namespace Recipoke.Api.Common.Application;

// Signature screening, not full decoding. Shared by recipe photos, covers and avatars.
public static class ImageTypes
{
    public static string? Detect(byte[] bytes)
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
