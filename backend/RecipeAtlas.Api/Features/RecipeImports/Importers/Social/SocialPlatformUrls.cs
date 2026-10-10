using Microsoft.AspNetCore.WebUtilities;
using System.Text.RegularExpressions;

namespace RecipeAtlas.Api.Features.RecipeImport;

public sealed record SocialSource(string Platform, Uri Url, string? Id, bool IsShortLink);

public static class SocialPlatformUrls
{
    private static readonly string[] Domains = ["youtube.com", "youtu.be", "tiktok.com", "instagram.com"];
    public static bool IsSocialHost(Uri url) => Domains.Any(domain =>
        url.IdnHost.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        url.IdnHost.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

    public static void ValidateHttps(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme != "https" || url.Port != 443 ||
            url.UserInfo.Length != 0 || url.AbsoluteUri.Length > 2048)
            throw new RecipeImportException("Use an HTTPS source URL without credentials or a custom port.",
                400, "invalid_source");
    }

    public static SocialSource Parse(Uri url)
    {
        ValidateHttps(url);
        var host = url.IdnHost.ToLowerInvariant();
        var path = url.AbsolutePath;
        string? id = null;
        if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtu.be")
        {
            if (host == "youtu.be") id = path.Trim('/');
            else if (path == "/watch")
            {
                var query = QueryHelpers.ParseQuery(url.Query);
                if (query.TryGetValue("v", out var values) && values.Count == 1) id = values[0];
            }
            else
            {
                var match = Regex.Match(path, @"^/(?:shorts|live|embed)/([A-Za-z0-9_-]{11})/?$");
                if (match.Success) id = match.Groups[1].Value;
            }
            if (id is not null && Regex.IsMatch(id, @"^[A-Za-z0-9_-]{11}$"))
                return new("youtube", new Uri("https://www.youtube.com/watch?v=" + id), id, false);
        }
        else if (host is "tiktok.com" or "www.tiktok.com" or "m.tiktok.com" or "vm.tiktok.com" or "vt.tiktok.com")
        {
            var match = Regex.Match(path, @"^/@[A-Za-z0-9_.-]{1,64}/video/([0-9]{6,25})/?$");
            if (match.Success && host is not ("vm.tiktok.com" or "vt.tiktok.com"))
                return new("tiktok", new Uri("https://www.tiktok.com" + path.TrimEnd('/')), match.Groups[1].Value, false);
            if ((host is "vm.tiktok.com" or "vt.tiktok.com" && Regex.IsMatch(path, @"^/[A-Za-z0-9]{4,64}/?$")) ||
                (host is "tiktok.com" or "www.tiktok.com" or "m.tiktok.com" && Regex.IsMatch(path, @"^/t/[A-Za-z0-9]{4,64}/?$")))
                return new("tiktok", new Uri(url.GetLeftPart(UriPartial.Path)), null, true);
        }
        else if (host is "instagram.com" or "www.instagram.com" or "m.instagram.com")
        {
            var match = Regex.Match(path, @"^/reels?/([A-Za-z0-9_-]{5,64})/?$");
            if (match.Success)
                return new("instagram", new Uri("https://www.instagram.com/reel/" + match.Groups[1].Value + "/"), match.Groups[1].Value, false);
            if (Regex.IsMatch(path, @"^/share/reel/[A-Za-z0-9_-]{5,64}/?$"))
                return new("instagram", new Uri(url.GetLeftPart(UriPartial.Path)), null, true);
        }
        throw new RecipeImportException("Use a public YouTube video/Short, TikTok video, or Instagram Reel link. Profiles and playlists are unsupported.",
            400, "unsupported_social_url", true);
    }
}
