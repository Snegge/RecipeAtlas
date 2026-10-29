using System.Text.RegularExpressions;

namespace Recipoke.Api.Infrastructure.Hosting;

public static partial class ApplicationShell
{
    public static void UseApplicationShell(this WebApplication app)
    {
        app.UseWhen(c => !c.Request.Path.StartsWithSegments("/api") && !c.Request.Path.StartsWithSegments("/health")
            && !c.Request.Path.StartsWithSegments("/openapi"), shell =>
        {
            shell.UseDefaultFiles();
            shell.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = ctx =>
            {
                var name = ctx.File.Name;
                ctx.Context.Response.Headers.CacheControl = HashedAsset().IsMatch(name)
                    ? "public,max-age=31536000,immutable"
                    : name.EndsWith(".png") || name.EndsWith(".svg") || name.EndsWith(".webmanifest")
                        ? "public,max-age=86400" : "no-cache";
                ctx.Context.Response.Headers.XContentTypeOptions = "nosniff";
                ctx.Context.Response.Headers["Referrer-Policy"] = "no-referrer";
            }});
            shell.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value ?? "/";
                var route = path.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
                    && !Path.HasExtension(path) && new[] { "", "login", "invite", "cookbooks", "shopping-lists", "account", "admin", "settings" }.Contains(route)
                    && app.Environment.WebRootFileProvider.GetFileInfo("index.html") is { Exists: true } index)
                {
                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.Headers.CacheControl = "no-cache";
                    context.Response.Headers["Referrer-Policy"] = "no-referrer";
                    await context.Response.SendFileAsync(index, context.RequestAborted);
                    return;
                }
                await next(context);
            });
        });
    }
    [GeneratedRegex(@"^(main|chunk|styles|polyfills)-[A-Za-z0-9_-]{8,}\.(js|css)$")]
    private static partial Regex HashedAsset();
}
