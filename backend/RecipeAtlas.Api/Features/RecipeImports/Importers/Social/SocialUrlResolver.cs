using System.Net;

namespace RecipeAtlas.Api.Features.RecipeImport;

public sealed class SocialUrlResolver(HttpClient httpClient, Func<Uri, CancellationToken, Task> validateHost)
{
    public async Task<SocialSource> ResolveAsync(Uri original, CancellationToken cancellationToken)
    {
        var initial = SocialPlatformUrls.Parse(original);
        await validateHost(original, cancellationToken);
        var current = initial;
        for (var redirects = 0; redirects <= 4; redirects++)
        {
            await validateHost(current.Url, cancellationToken);
            if (!current.IsShortLink) return current;
            using var request = new HttpRequestMessage(HttpMethod.Get, current.Url);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or
                HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                throw new RecipeImportException("The share link could not be resolved. Open it and paste the full video link or paste its description.",
                    502, "social_redirect_failed", true);
            if (redirects == 4 || response.Headers.Location is not { } location ||
                !Uri.TryCreate(current.Url, location.ToString(), out var next))
                throw new RecipeImportException("The share link returned an invalid or excessive redirect. Paste the full video link or description.",
                    422, "social_redirect_failed", true);
            var resolved = SocialPlatformUrls.Parse(next);
            if (resolved.Platform != initial.Platform)
                throw new RecipeImportException("The share link redirected outside its supported platform. Paste the description instead.",
                    400, "unsafe_redirect", true);
            await validateHost(next, cancellationToken);
            current = resolved;
        }
        throw new InvalidOperationException("Unreachable redirect state.");
    }
}
