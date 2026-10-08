using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RecipeAtlas.Api;

if (args.Contains("--hash-password"))
{
    OwnerAuth.PrintPasswordHash();
    return;
}

var builder = WebApplication.CreateBuilder(args);
var dataDirectory = Path.GetFullPath(builder.Configuration["DataDirectory"] ?? "data",
    builder.Environment.ContentRootPath);
Directory.CreateDirectory(dataDirectory);
var connection = new SqliteConnectionStringBuilder
{
    DataSource = Path.Combine(dataDirectory, "recipes.db"), ForeignKeys = true
}.ToString();

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 6 * 1024 * 1024);
builder.Services.AddDbContext<RecipeDb>(options => options.UseSqlite(connection));
builder.Services.AddProblemDetails();
builder.Services.AddDataProtection().SetApplicationName("RecipeAtlas")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));

var passwordHash = builder.Configuration["Owner:PasswordHash"];
if (string.IsNullOrWhiteSpace(passwordHash))
    throw new InvalidOperationException(
        "Set Owner:PasswordHash using user-secrets locally or Owner__PasswordHash in production. See README.md.");
var owner = new OwnerAuth(passwordHash);
builder.Services.AddSingleton(owner);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "RecipeAtlas.Session";
        options.Cookie.Path = "/api";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = false;
        options.Events.OnRedirectToLogin = context =>
        { context.Response.StatusCode = 401; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = context =>
        { context.Response.StatusCode = 403; return Task.CompletedTask; };
        options.Events.OnValidatePrincipal = async context =>
        {
            // Changing the configured password hash and restarting revokes older sessions.
            if (context.Principal?.FindFirst("stamp")?.Value != owner.Stamp)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync();
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    // Single-owner application: one shared login budget avoids trusting proxy IP headers.
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(15);
        limiter.QueueLimit = 0;
    });
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();

// Browser writes must carry this non-safelisted header. Cross-origin websites cannot
// send it without CORS preflight approval. This API deliberately enables no CORS.
// Use a development proxy and the same public origin for frontend and API.
app.Use(async (context, next) =>
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
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Deliberate single-instance deployment. Back up before upgrading the schema.
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<RecipeDb>().Database.MigrateAsync();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
OwnerAuth.MapEndpoints(app);
RecipeEndpoints.Map(app);
app.Run();
