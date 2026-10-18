using Recipoke.Api.Features.Accounts.Application;
namespace Recipoke.Api.Features.Authentication.Api;
public static class AuthenticationEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/auth/login", async (LoginInput input, AccountAuthentication operation, ISessionCookies sessions, CancellationToken ct) =>
        {
            var id = await operation.AuthenticateAsync(input.Username, input.Password, ct);
            if (id is null) return Results.Problem(statusCode: 401, title: "Invalid username or password.");
            await sessions.SignInAsync(id.Value); return Results.NoContent();
        }).RequireRateLimiting("login").Produces(204).ProducesProblem(401);
        app.MapGet("/api/auth/me", async (AccountAuthentication operation, CancellationToken ct) => Results.Ok(await operation.CurrentAsync(ct)))
            .RequireAuthorization().Produces<CurrentUserResponse>();
        app.MapPost("/api/auth/logout", async (ISessionCookies sessions) => { await sessions.SignOutAsync(); return Results.NoContent(); })
            .RequireAuthorization().Produces(204);
        app.MapPost("/api/auth/password", async (ChangePasswordInput input, AccountAuthentication operation, ISessionCookies sessions, CancellationToken ct) =>
        {
            var id = await operation.ChangePasswordAsync(input, ct); await sessions.SignInAsync(id); return Results.NoContent();
        }).RequireAuthorization().Produces(204).ProducesValidationProblem();
    }
}
