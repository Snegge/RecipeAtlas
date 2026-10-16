namespace Recipoke.Api.Common.Http;

public sealed class BrowserWriteProtection(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers.CacheControl = "no-store";
        if (context.Request.Path.StartsWithSegments("/api") &&
            !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) ||
              HttpMethods.IsOptions(context.Request.Method)) &&
            context.Request.Headers["X-RecipeAtlas"] != "1")
        {
            await Results.Problem(statusCode: 403, title: "Missing X-RecipeAtlas: 1 header.")
                .ExecuteAsync(context);
            return;
        }
        await next(context);
    }
}
