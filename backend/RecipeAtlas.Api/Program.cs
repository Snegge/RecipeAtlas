using Recipoke.Api.Infrastructure.Hosting;
using Recipoke.Api.Common.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Recipoke.Api.Features.Cookbooks.Api;
using Recipoke.Api.Features.Accounts;
using Recipoke.Api.Features.Accounts.Application;
using Recipoke.Api.Features.Accounts.Api;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Features.Authentication.Api;
using Recipoke.Api.Features.Authentication.Infrastructure;
using Recipoke.Api.Features.Recipes;
using Recipoke.Api.Features.Recipes.Api;
using Recipoke.Api.Features.RecipeImports;
using Recipoke.Api.Features.RecipeImports.Api;
using Recipoke.Api.Features.Shopping;
using Recipoke.Api.Features.Shopping.Api;
using Recipoke.Api.Features.Shopping.Infrastructure;

if (args.Length == 2 && args[0] == "--generate-protection-certificate")
{
    ProtectionCertificate.Generate(args[1]);
    return;
}
var builder = WebApplication.CreateBuilder(args);
var administration = args.Any(x => x is "--migrate" or "--migration-status" or "--bootstrap-admin" or "--recover-admin");
var postgres = builder.AddPersistence(administration);
if (builder.Configuration["PORT"] is { } port)
{
    if (!int.TryParse(port, out var number) || number is < 1 or > 65535) throw new InvalidOperationException("Invalid PORT.");
    builder.WebHost.UseUrls($"http://0.0.0.0:{number}");
}
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 6 * 1024 * 1024);
builder.Services.AddProblemDetails();
builder.Services.AddApiDescription();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<AccountOptions>(builder.Configuration.GetSection("Accounts"));
builder.Services.AddIdentityCore<AppUser>(options =>
{
    options.Password.RequiredLength = 12; options.Password.RequireDigit = false;
    options.Password.RequireUppercase = false; options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._";
    options.Lockout.MaxFailedAccessAttempts = 5; options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<RecipeDb>().AddSignInManager().AddDefaultTokenProviders();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddCookie(IdentityConstants.ApplicationScheme, options =>
{
    // New cookie and Identity scheme deliberately reject every old single-owner session.
    options.Cookie.Name = "Recipoke.Session"; options.Cookie.Path = "/api";
    options.Cookie.HttpOnly = true; options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromDays(14); options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
    options.Events.OnValidatePrincipal = ActiveSessionValidation.ValidateAsync;
});
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<ISessionCookies, IdentitySessions>();
builder.Services.AddScoped<AccountAccess>(); builder.Services.AddScoped<AccountAuthentication>();
builder.Services.AddScoped<AdministratorSetup>(); builder.Services.AddScoped<AdministratorAccounts>();
builder.Services.AddScoped<AccountProfiles>();
builder.Services.AddScoped<CookbookInvitations>(); builder.Services.AddScoped<CookbookMedia>();
builder.Services.AddScoped<Invitations>(); builder.Services.AddScoped<CookbookAccess>(); builder.Services.AddScoped<CookbookOperations>();
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    // Small single-instance deployment: shared login budget avoids trusting proxy IP headers.
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(15);
        limiter.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("invitation", limiter =>
    {
        limiter.PermitLimit = 30; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("recipe-import", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});

builder.Services.AddRecipeOperations();
builder.Services.AddRecipeImport();
builder.Services.AddShoppingOperations();
builder.Services.AddShoppingRealtime();

if (postgres && !administration)
{
    if (!Uri.TryCreate(builder.Configuration["Accounts:PublicAppUrl"], UriKind.Absolute, out var publicUrl)
        || publicUrl.Scheme != "https" || publicUrl.UserInfo.Length != 0 || publicUrl.Query.Length != 0 || publicUrl.Fragment.Length != 0
        || publicUrl.AbsolutePath != "/") throw new InvalidOperationException("Accounts:PublicAppUrl must be an HTTPS origin.");
}
var app = builder.Build();
// Render terminates TLS. Cookies are explicitly Secure; trusted links use PublicAppUrl, not forwarded headers.
app.UseApplicationShell();
// Deliberate exposure policy: no documentation route in Production.
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // Minimal API binding can throw in Development. Malformed/string-contract
    // request bodies are client errors, including legacy numeric quantities.
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
        ? badRequest.StatusCode : StatusCodes.Status500InternalServerError
});
app.UseStatusCodePages();

// Browser writes must carry this non-safelisted header. Cross-origin websites cannot
// send it without CORS preflight approval. This API deliberately enables no CORS.
// Use a development proxy and the same public origin for frontend and API.
app.UseMiddleware<BrowserWriteProtection>();
app.UseMiddleware<OperationFailures>();
app.UseMiddleware<AccountSetupGate>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RecipeDb>();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    if (!postgres || args.Contains("--migrate")) await db.Database.MigrateAsync(timeout.Token);
    var pending = (await db.Database.GetPendingMigrationsAsync(timeout.Token)).ToArray();
    if (args.Contains("--migration-status"))
    {
        Console.WriteLine($"Pending migrations: {pending.Length}");
        Environment.ExitCode = pending.Length == 0 ? 0 : 2;
        return;
    }
    if (pending.Length != 0) throw new InvalidOperationException("Schema upgrade required. Run --migrate before starting the application.");
    if (args.Contains("--migrate")) { Console.WriteLine("Schema is current."); return; }
}

app.MapGet("/health", (HttpContext context) =>
{
    context.Response.Headers["X-Recipoke-Backend"] = app.Configuration["Release:Backend"] ?? "local";
    context.Response.Headers["X-Recipoke-Frontend"] = app.Configuration["Release:Frontend"] ?? "local";
    return Results.Ok(new { status = "ok" });
});

if (args.Contains("--bootstrap-admin") || args.Contains("--recover-admin"))
{
    try { await AdministratorCommands.RunAsync(app.Services, args.Contains("--recover-admin")); }
    catch (OperationFailure failure) { Console.Error.WriteLine(failure.Message); Environment.ExitCode = 1; }
    return;
}

AuthenticationEndpoints.Map(app);
AccountEndpoints.Map(app);
CookbookEndpoints.Map(app);
RecipeEndpoints.Map(app);
RecipeImportEndpoints.Map(app);
ShoppingEndpoints.Map(app);
app.MapShoppingRealtime();

app.Run();
