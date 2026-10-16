using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Recipoke.Api.Features.Authentication.Infrastructure;

namespace Recipoke.Api.Features.Authentication.Api;

public static class AuthenticationEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/auth/login", async (LoginInput input, OwnerAuth owner, HttpContext context) =>
        {
            if (!owner.Verify(input.Password)) return Results.Unauthorized();
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "owner"), new Claim("stamp", owner.Stamp)],
                CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
            return Results.NoContent();
        }).RequireRateLimiting("login");

        app.MapGet("/api/auth/me", () => Results.Ok(new { name = "owner" }))
            .RequireAuthorization();

        app.MapPost("/api/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
