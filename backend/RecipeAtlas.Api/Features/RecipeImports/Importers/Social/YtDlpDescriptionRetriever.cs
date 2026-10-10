using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace RecipeAtlas.Api.Features.RecipeImport;

public sealed class SocialImportOptions
{
    public string YtDlpPath { get; set; } = "yt-dlp";
    public string NodePath { get; set; } = "node";
}
public interface ISocialDescriptionRetriever
{
    Task<string> RetrieveAsync(SocialSource source, CancellationToken cancellationToken);
}
public sealed class YtDlpDescriptionRetriever(IMetadataProcessRunner runner, IOptions<SocialImportOptions> options) : ISocialDescriptionRetriever
{
    public static IReadOnlyList<string> Arguments(SocialSource source, string nodePath)
    {
        var flags = new OptionSet
        {
            IgnoreConfig = true, NoPlaylist = true, SkipDownload = true, Simulate = true,
            IgnoreNoFormatsError = true, DumpSingleJson = true,
            NoWriteSubs = true, NoWriteAutoSubs = true, NoWriteComments = true,
            NoWriteThumbnail = true, NoWriteInfoJson = true
        }.GetOptionFlags().ToList();
        flags.AddRange(["--no-plugin-dirs", "--no-cache-dir", "--no-remote-components",
            "--socket-timeout", "10", "--retries", "0", "--extractor-retries", "0",
            "--no-js-runtimes", "--js-runtimes", "node:" + nodePath,
            "--use-extractors", "Youtube,TikTok,Instagram", "--", source.Url.AbsoluteUri]);
        return flags;
    }
    public async Task<string> RetrieveAsync(SocialSource source, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(options.Value.YtDlpPath, Arguments(source, options.Value.NodePath), cancellationToken);
        if (result.ExitCode != 0)
            throw new RecipeImportException("The platform blocked caption retrieval, requires sign-in, or could not be reached. Open the public post and paste its description instead.",
                502, "social_retrieval_failed", true);
        VideoData? metadata;
        try { metadata = JsonConvert.DeserializeObject<VideoData>(result.Output, new JsonSerializerSettings { MaxDepth = 32 }); }
        catch (JsonException)
        {
            throw new RecipeImportException("The platform returned invalid metadata. Paste the description instead.", 502, "invalid_metadata", true);
        }
        // Instagram may use a numeric media ID while the URL uses a shortcode.
        if (metadata is null || (source.Platform == "instagram"
            ? metadata.DisplayID != source.Id && !InstagramPageMatches(metadata.WebpageUrl, source.Id)
            : metadata.ID != source.Id))
            throw new RecipeImportException("The platform did not return metadata for the requested video. Paste the description instead.", 502, "invalid_metadata", true);
        if (string.IsNullOrWhiteSpace(metadata.Description))
            throw new RecipeImportException("This post has no written description or caption. Description-only import cannot read the video, comments, or bio links.",
                422, "missing_description", true);
        RecipeTextExtractor.ValidateSource(metadata.Description);
        return metadata.Description;
    }
    private static bool InstagramPageMatches(string? page, string? id) =>
        Uri.TryCreate(page, UriKind.Absolute, out var url) && url.Scheme == "https" &&
        url.IdnHost is "www.instagram.com" or "instagram.com" &&
        System.Text.RegularExpressions.Regex.IsMatch(url.AbsolutePath, "^/(?:p|reels?)/" + System.Text.RegularExpressions.Regex.Escape(id ?? "") + "/?$");
}
