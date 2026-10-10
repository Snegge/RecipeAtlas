using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RecipeAtlas.Api.Features.RecipeImport;

internal static class SocialImportTests
{
    private const string English = "Weeknight beef\nServes 2\n1-2 lb beef\n1/2 US cup milk\n3-4 eggs\nsalt to taste\nMix everything.\nCook until done.";
    private static ExtractedRecipe Complete => new()
    {
        Title = "Weeknight beef", Description = null, Servings = 2, OriginalYield = "Serves 2",
        Ingredients = ["1-2 lb beef", "1/2 US cup milk", "3-4 eggs", "salt to taste"],
        Steps = ["Mix everything.", "Cook until done."], Flags = [], UsableRecipe = true
    };
    private static string Json(ExtractedRecipe recipe) => JsonSerializer.Serialize(recipe, RecipeTextExtractor.JsonOptions);

    public static async Task RunAsync(Action<bool, string> check)
    {
        async Task Failure(Func<Task> operation, string code)
        {
            try { await operation(); throw new Exception("Expected import failure " + code); }
            catch (RecipeImportException error) { check(error.Code == code, "Import failure: " + code + " got " + error.Code); }
        }
        foreach (var (url, platform, canonical) in new[]
        {
            ("https://youtu.be/BaW_jenozKc?si=tracking", "youtube", "https://www.youtube.com/watch?v=BaW_jenozKc"),
            ("https://m.youtube.com/watch?v=BaW_jenozKc&list=discard", "youtube", "https://www.youtube.com/watch?v=BaW_jenozKc"),
            ("https://www.youtube.com/shorts/BaW_jenozKc", "youtube", "https://www.youtube.com/watch?v=BaW_jenozKc"),
            ("https://m.tiktok.com/@cook/video/1234567890123456789?share=1", "tiktok", "https://www.tiktok.com/@cook/video/1234567890123456789"),
            ("https://www.instagram.com/reel/AbCde_12345/?igsh=tracking", "instagram", "https://www.instagram.com/reel/AbCde_12345/")
        })
        {
            var parsed = SocialPlatformUrls.Parse(new Uri(url));
            check(parsed.Platform == platform && parsed.Url.AbsoluteUri == canonical && !parsed.IsShortLink, "Social platform normalization");
        }
        foreach (var url in new[] { "http://youtu.be/BaW_jenozKc", "https://user:password@youtube.com/watch?v=BaW_jenozKc", "https://youtube.com:8443/watch?v=BaW_jenozKc",
            "https://127.0.0.1/reel/AbCde_12345", "https://youtube.com.evil.invalid/watch?v=BaW_jenozKc", "https://youtube.com/playlist?list=abc",
            "https://evil.youtube.com/watch?v=BaW_jenozKc", "https://tiktok.com/@cook", "https://instagram.com/accounts/login/" })
        {
            try { SocialPlatformUrls.Parse(new Uri(url)); throw new Exception("Unsafe social URL accepted"); }
            catch (RecipeImportException) { check(true, "Unsafe/unsupported social URL rejected"); }
        }
        foreach (var address in new[] { "127.0.0.1", "10.0.0.1", "169.254.169.254", "192.168.1.1", "100.64.0.1", "::1", "::ffff:127.0.0.1" })
            check(!SafeWebsiteClient.IsPublicIPv4(IPAddress.Parse(address)), "Private network blocked");
        check(SafeWebsiteClient.IsPublicIPv4(IPAddress.Parse("8.8.8.8")), "Public IPv4 permitted");
        foreach (var address in new[] { "::1", "::ffff:127.0.0.1", "fc00::1", "fe80::1", "2001:db8::1", "2001::1" })
            check(!SafeWebsiteClient.IsPublicAddress(IPAddress.Parse(address)), "Private/reserved IPv6 rejected before external retrieval");
        check(SafeWebsiteClient.IsPublicAddress(IPAddress.Parse("2606:4700:4700::1111")), "Public IPv6 permitted alongside public IPv4");

        foreach (var shortUrl in new[] { "https://vm.tiktok.com/AbCde12/", "https://vt.tiktok.com/AbCde12/", "https://www.tiktok.com/t/AbCde12/" })
        {
            var checkedHosts = new List<string>();
            var handler = new FakeHttp(_ => Redirect("https://www.tiktok.com/@cook/video/1234567890123456789?tracking=1"));
            var resolver = new SocialUrlResolver(new HttpClient(handler), (uri, _) => { checkedHosts.Add(uri.Host); return Task.CompletedTask; });
            var resolved = await resolver.ResolveAsync(new Uri(shortUrl), default);
            check(resolved.Id == "1234567890123456789" && checkedHosts.Last() == "www.tiktok.com", "Short-link redirect and public-host validation");
            check(handler.Calls == 1, "Only the selected short-link host requested");
        }
        foreach (var redirect in new[] { "http://www.tiktok.com/@cook/video/1234567890123456789", "https://127.0.0.1/", "https://evil.invalid/", "https://www.youtube.com/watch?v=BaW_jenozKc" })
        {
            var resolver = new SocialUrlResolver(new HttpClient(new FakeHttp(_ => Redirect(redirect))), (_, _) => Task.CompletedTask);
            try { await resolver.ResolveAsync(new Uri("https://vm.tiktok.com/AbCde12/"), default); throw new Exception("Unsafe redirect accepted"); }
            catch (RecipeImportException) { check(true, "Unsafe/cross-platform redirect rejected before process launch"); }
        }
        await Failure(() => new SocialUrlResolver(new HttpClient(new FakeHttp(_ => Redirect("https://vt.tiktok.com/AbCde12/"))), (_, _) => Task.CompletedTask)
            .ResolveAsync(new Uri("https://vm.tiktok.com/AbCde12/"), default), "social_redirect_failed");
        await Failure(() => new SocialUrlResolver(new HttpClient(new FakeHttp(_ => new(HttpStatusCode.Forbidden))), (_, _) => Task.CompletedTask)
            .ResolveAsync(new Uri("https://vm.tiktok.com/AbCde12/"), default), "social_redirect_failed");
        var instagramShare = await new SocialUrlResolver(new HttpClient(new FakeHttp(_ => Redirect("https://www.instagram.com/reel/AbCde_12345/?igsh=tracking"))), (_, _) => Task.CompletedTask)
            .ResolveAsync(new Uri("https://www.instagram.com/share/reel/AbCde_12345/"), default);
        check(instagramShare.Id == "AbCde_12345" && !instagramShare.IsShortLink, "Instagram share link resolved to reel");
        await Failure(() => new SocialUrlResolver(new HttpClient(new FakeHttp(_ => Redirect("https://www.tiktok.com/@cook/video/1234567890123456789"))),
            (uri, _) => uri.Host == "www.tiktok.com" ? throw new RecipeImportException("Private redirect DNS", 400, "unsafe_source") : Task.CompletedTask)
            .ResolveAsync(new Uri("https://vm.tiktok.com/AbCde12/"), default), "unsafe_source");

        var fakeModel = new FakeModel(Json(Complete));
        var extractor = new RecipeTextExtractor(fakeModel);
        var complete = await extractor.ExtractAsync(English, "https://youtu.be/BaW_jenozKc", "youtube");
        check(fakeModel.Input == English, "Model receives full original caption and line breaks, not URL/title");
        check(complete.Draft.Title == "Weeknight beef" && complete.Draft.Servings == 2, "Explicit source title and yield");
        check(complete.Draft.Ingredients[0].Quantity == "453.592-907.185", "Both pound range endpoints converted");
        check(complete.Draft.Ingredients[1].Quantity == "118.294" && complete.Draft.Ingredients[1].Unit == "ml", "Fractional US cup volume conversion");
        check(complete.Draft.Ingredients[2].Quantity == "3-4" && complete.Draft.Ingredients[2].Unit == "piece", "Missing unit piece default");
        check(complete.Draft.Ingredients[3].Quantity == "" && complete.Draft.Ingredients[3].Unit == "toTaste", "toTaste string quantity retained");
        check(complete.Draft.Ingredients.All(i => i.RequiresReview) && complete.Draft.Ingredients[0].OriginalText == "1-2 lb beef", "AI ingredient review and original wording preserved");
        check(complete.Draft.ExternalImageUrl is null && complete.SourceType == "youtube", "No thumbnail required for success");

        const string german = "Pfannkuchen\n2 Portionen\n1 1/2 kg Mehl (gesiebt)\n3–4 Eier\n0,5 l Milch\nFett für das Blech\nZubereitung in den Kommentaren.";
        var partialRecipe = new ExtractedRecipe { Title = "Pfannkuchen", Servings = 2, OriginalYield = "2 Portionen",
            Ingredients = ["1 1/2 kg Mehl (gesiebt)", "3–4 Eier", "0,5 l Milch", "Fett für das Blech"], Steps = [], Flags = ["Zubereitung fehlt."],
            UsableRecipe = true, RefersElsewhere = true };
        var partial = await new RecipeTextExtractor(new FakeModel(Json(partialRecipe))).ExtractAsync(german, null, "text");
        check(partial.Draft.Title == "Pfannkuchen" && partial.Draft.Ingredients[0].Quantity == "1 1/2", "German wording and mixed fractions");
        check(partial.Draft.Ingredients[0].Note == "gesiebt" && partial.Draft.Ingredients[1].Quantity == "3-4", "Notes and Unicode range separator retained");
        check(partial.Draft.Ingredients[2].Quantity == "0.5" && partial.Draft.Ingredients[3].Quantity == "", "Decimal commas and missing amounts");
        check(partial.Draft.Steps.Count == 0 && partial.Warnings.Any(w => w.Contains("partial draft")), "Missing instructions produce partial editable draft");
        check(partial.Draft.SourceUrl == "" && partial.Warnings.Any(w => w.Contains("comments")), "Standalone pasted text and description-only limitation");
        var absentYield = new ExtractedRecipe { Title = "Weeknight beef", Ingredients = ["1-2 lb beef"], Steps = ["Mix everything."], UsableRecipe = true };
        var noYield = await new RecipeTextExtractor(new FakeModel(Json(absentYield))).ExtractAsync(English, null, "text");
        check(noYield.Draft.Servings is null && noYield.Warnings.Any(w => w.Contains("Servings")), "No invented serving default");

        foreach (var invalid in new[] { "not json", "{}", Json(Complete).Replace("1-2 lb beef", "5 lb salmon"), Json(Complete).Replace("Mix everything.", "Bake at 450°F for 15 minutes."),
            Json(Complete).Replace("\"servings\":2", "\"servings\":9"), Json(Complete).Replace("\"flags\":[]", "\"flags\":[null]"),
            Json(Complete).Replace("\"title\":\"Weeknight beef\"", "\"title\":\"Invented title\""),
            JsonSerializer.Serialize(new { title = "Weeknight beef", ingredients = Enumerable.Repeat("1-2 lb beef", 101).ToArray() }) })
            await Failure(() => new RecipeTextExtractor(new FakeModel(invalid)).ExtractAsync(English, null, "text"), "invalid_model_response");
        await Failure(() => extractor.ExtractAsync(new string('x', 20001), null, "text"), "invalid_text");
        var empty = new ExtractedRecipe { Ingredients = [], Steps = [], Flags = [], UsableRecipe = false };
        await Failure(() => new RecipeTextExtractor(new FakeModel(Json(empty))).ExtractAsync("A beautiful sunset", null, "text"), "no_recipe");
        await Failure(() => new RecipeTextExtractor(new FakeModel(Json(empty))).ExtractAsync("Recipe in comments. Link in bio.", null, "text"), "recipe_elsewhere");
        var multiple = new ExtractedRecipe { Ingredients = [], Steps = [], Flags = [], MultipleRecipes = true };
        await Failure(() => new RecipeTextExtractor(new FakeModel(Json(multiple))).ExtractAsync("Recipe one and recipe two", null, "text"), "multiple_recipes");

        var source = SocialPlatformUrls.Parse(new Uri("https://youtu.be/BaW_jenozKc"));
        var arguments = YtDlpDescriptionRetriever.Arguments(source, "node");
        foreach (var option in new[] { "--simulate", "--skip-download", "--ignore-no-formats-error", "--dump-single-json", "--no-write-subs", "--no-write-auto-subs", "--no-write-comments", "--no-remote-components", "--ignore-config" })
            check(arguments.Contains(option) || (option == "--simulate" && arguments.Contains("-s")) ||
                (option == "--dump-single-json" && arguments.Contains("-J")), "Explicit metadata-only option: " + option);
        check(arguments[^2] == "--" && arguments[^1] == source.Url.AbsoluteUri && !arguments.Any(a => a.Contains("ffmpeg")), "Safe option boundary, no FFmpeg/media dependency");
        var metadataRunner = new FakeProcess(new(0, JsonSerializer.Serialize(new { id = source.Id, title = "Not a recipe title", description = English }), ""));
        var retriever = new YtDlpDescriptionRetriever(metadataRunner, Options.Create(new SocialImportOptions()));
        check(await retriever.RetrieveAsync(source, default) == English, "Full yt-dlp description preserved");
        foreach (var caption in new string?[] { null, "", "   " })
            await Failure(() => new YtDlpDescriptionRetriever(new FakeProcess(new(0, JsonSerializer.Serialize(new { id = source.Id, title = "Recipe title", description = caption }), "")), Options.Create(new SocialImportOptions()))
                .RetrieveAsync(source, default), "missing_description");
        await Failure(() => new YtDlpDescriptionRetriever(new FakeProcess(new(1, "", "private credentials must not appear")), Options.Create(new SocialImportOptions())).RetrieveAsync(source, default), "social_retrieval_failed");
        await Failure(() => new YtDlpDescriptionRetriever(new FakeProcess(new(0, "invalid", "")), Options.Create(new SocialImportOptions())).RetrieveAsync(source, default), "invalid_metadata");
        var ig = SocialPlatformUrls.Parse(new Uri("https://instagram.com/reel/AbCde_12345/"));
        check(await new YtDlpDescriptionRetriever(new FakeProcess(new(0, JsonSerializer.Serialize(new { id = "numeric-media-id", webpage_url = "https://www.instagram.com/p/AbCde_12345/", description = English }), "")),
            Options.Create(new SocialImportOptions())).RetrieveAsync(ig, default) == English, "Instagram shortcode/numeric ID metadata");

        var noFetch = new FakeHttp(_ => throw new Exception("Normal social URL should not use redirect HTTP client"));
        var social = new SocialRecipeImporter(new(new HttpClient(noFetch), (_, _) => Task.CompletedTask), retriever, extractor);
        var socialResult = await social.ImportAsync(new Uri("https://youtu.be/BaW_jenozKc"));
        check(socialResult.SourceType == "youtube" && socialResult.Draft.SourceUrl == "https://youtu.be/BaW_jenozKc", "Shared retriever/model and original URL handoff");
        var webHtml = "<script type=\"application/ld+json\">{\"@type\":\"Recipe\",\"name\":\"Website\",\"recipeIngredient\":[\"3-4 Eier\"],\"recipeInstructions\":[\"Cook\"]}</script>";
        var website = new WebsiteRecipeImporter(new(new HttpClient(new FakeHttp(_ => new(HttpStatusCode.OK) { Content = new StringContent(webHtml, System.Text.Encoding.UTF8, "text/html") }))), new());
        var unconfigured = new RecipeTextExtractor(new GeminiRecipeTextModel(new HttpClient(noFetch), Options.Create(new RecipeTextOptions())));
        var service = new RecipeImportService([social, website], unconfigured);
        check((await service.ImportAsync("https://example.com/recipe")).Draft.Ingredients[0].Quantity == "3-4", "Website import independent of missing Gemini configuration");
        await Failure(() => service.ImportTextAsync(English, null), "ai_not_configured");
        var textService = new RecipeImportService([], extractor);
        check((await textService.ImportTextAsync(English, "https://youtu.be/BaW_jenozKc")).Draft.SourceUrl == "https://youtu.be/BaW_jenozKc", "Pasted-caption fallback keeps social attribution without fetching");
        check((await textService.ImportTextAsync(English, null)).Draft.SourceUrl == "", "Standalone text request");
        await Failure(() => textService.ImportTextAsync(English, "https://127.0.0.1/"), "invalid_source");
        await Failure(() => textService.ImportTextAsync(English, "https://[::ffff:127.0.0.1]/"), "invalid_source");
        var dnsFailed = new SocialRecipeImporter(new(new HttpClient(noFetch), (_, _) => throw new System.Net.Sockets.SocketException()), retriever, extractor);
        await Failure(() => new RecipeImportService([dnsFailed, website], extractor).ImportAsync("https://youtu.be/BaW_jenozKc"), "social_retrieval_failed");

        await TestGeminiAsync(check, Failure);
        await TestProcessesAsync(check, Failure);
    }

    private static async Task TestGeminiAsync(Action<bool, string> check, Func<Func<Task>, string, Task> failure)
    {
        var handler = new AsyncHttp(async (request, token) =>
        {
            check(request.RequestUri!.AbsolutePath.EndsWith("/gemini-flash-latest:generateContent"), "Documented Gemini endpoint/model alias");
            check(request.Headers.Contains("x-goog-api-key") && !request.RequestUri.Query.Contains("key"), "API key only in backend header");
            using var sent = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var root = sent.RootElement;
            check(root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString() == English, "Gemini receives actual description text");
            check(root.GetProperty("generationConfig").GetProperty("responseMimeType").GetString() == "application/json" &&
                root.GetProperty("generationConfig").TryGetProperty("responseJsonSchema", out _), "Official structured JSON API fields");
            check(!root.TryGetProperty("tools", out _) && root.GetProperty("systemInstruction").ToString().Contains("untrusted"), "Untrusted source, no tools/link browsing");
            var envelope = new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = Json(Complete) } } } } } };
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(envelope)) };
        });
        var settings = Options.Create(new RecipeTextOptions { ApiKey = "isolated-fixture-value" });
        check(await new GeminiRecipeTextModel(new(handler), settings).ExtractAsync(English, default) == Json(Complete), "Structured Gemini response consumed");
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests })
            await failure(() => new GeminiRecipeTextModel(new(new FakeHttp(_ => new(status))), settings).ExtractAsync(English, default), "ai_unavailable");
        await failure(() => new GeminiRecipeTextModel(new(new FakeHttp(_ => new(HttpStatusCode.OK) { Content = new StringContent("{}") })), settings).ExtractAsync(English, default), "invalid_model_response");
        foreach (var envelope in new[] {
            "{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\"}]}",
            "{\"candidates\":[]}", new string('x', 128 * 1024 + 1) })
            await failure(() => new GeminiRecipeTextModel(new(new FakeHttp(_ => new(HttpStatusCode.OK) { Content = new StringContent(envelope) })), settings)
                .ExtractAsync(English, default), "invalid_model_response");
        await failure(() => new GeminiRecipeTextModel(new(new AsyncHttp((_, _) => throw new TaskCanceledException())), settings).ExtractAsync(English, default), "ai_timeout");
    }

    private static async Task TestProcessesAsync(Action<bool, string> check, Func<Func<Task>, string, Task> failure)
    {
        var executable = Environment.ProcessPath!;
        var prefix = new List<string>();
        if (Path.GetFileNameWithoutExtension(executable) == "dotnet") prefix.Add(typeof(SocialImportTests).Assembly.Location);
        IReadOnlyList<string> Args(params string[] args) => prefix.Concat(["--process-fixture"]).Concat(args).ToArray();
        var runner = new BoundedProcessRunner();
        const string argument = "safe value;$(never)\"quote";
        check((await runner.RunAsync(executable, Args("argument", argument), default)).Output == argument, "External arguments passed without a shell");
        await failure(() => runner.RunAsync(executable, Args("output"), default), "metadata_too_large");
        await failure(() => runner.RunAsync(executable, Args("error"), default), "metadata_too_large");
        await failure(() => runner.RunAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid()+"missing-executable"), [], default), "metadata_unavailable");
        var directory = Path.Combine(Path.GetTempPath(), "social-process-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var pidFiles = new[] { Path.Combine(directory, "one.pid"), Path.Combine(directory, "two.pid") };
            using var cancel = new CancellationTokenSource();
            var tasks = pidFiles.Select(file => runner.RunAsync(executable, Args("wait", file), cancel.Token)).ToArray();
            for (var i = 0; i < 200 && !pidFiles.All(File.Exists); i++) await Task.Delay(10);
            check(pidFiles.All(File.Exists), "Both bounded-concurrency fixture processes started");
            await failure(() => runner.RunAsync(executable, Args("argument", "third"), default), "import_busy");
            cancel.Cancel();
            foreach (var task in tasks)
            {
                try { await task; throw new Exception("Cancellation ignored"); }
                catch (OperationCanceledException) { check(true, "Request cancellation propagated"); }
            }
            foreach (var file in pidFiles)
            {
                try { using var child = Process.GetProcessById(int.Parse(File.ReadAllText(file))); check(child.HasExited, "Canceled child terminated"); }
                catch (ArgumentException) { check(true, "Canceled child removed"); }
            }
            await failure(() => new BoundedProcessRunner(TimeSpan.FromMilliseconds(250)).RunAsync(executable, Args("wait", Path.Combine(directory, "timeout.pid")), default), "metadata_timeout");
        }
        finally { Directory.Delete(directory, true); }
    }

    public static async Task LiveAsync(string[] urls)
    {
        var path = Environment.GetEnvironmentVariable("SocialImport__YtDlpPath") ?? "yt-dlp";
        var retriever = new YtDlpDescriptionRetriever(new BoundedProcessRunner(), Options.Create(new SocialImportOptions { YtDlpPath = path }));
        using var client = new HttpClient(SafeWebsiteClient.CreateHandler());
        var resolver = new SocialUrlResolver(client, SafeWebsiteClient.ValidatePublicHostAsync);
        foreach (var url in urls)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            var uri = new Uri(url);
            try
            {
                var source = await resolver.ResolveAsync(uri, timeout.Token);
                var text = await retriever.RetrieveAsync(source, timeout.Token);
                Console.WriteLine(source.Platform + ": metadata-only retrieval succeeded; description characters=" + text.Length + "; no model call.");
            }
            catch (RecipeImportException error) { Console.WriteLine(uri.Host + ": " + error.Code + ": " + error.Message); }
            catch (Exception error) when (error is HttpRequestException or System.Net.Sockets.SocketException or OperationCanceledException) { Console.WriteLine(uri.Host + ": network/DNS/timeout blocked metadata retrieval."); }
        }
    }
    private static HttpResponseMessage Redirect(string url) => new(HttpStatusCode.Found) { Headers = { Location = new Uri(url) } };
    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; return Task.FromResult(response(request)); }
    }
    private sealed class AsyncHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request, cancellationToken);
    }
    private sealed class FakeModel(string result) : IRecipeTextModel
    {
        public string? Input { get; private set; }
        public Task<string> ExtractAsync(string sourceText, CancellationToken cancellationToken) { Input = sourceText; return Task.FromResult(result); }
    }
    private sealed class FakeProcess(MetadataProcessResult result) : IMetadataProcessRunner
    {
        public Task<MetadataProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
