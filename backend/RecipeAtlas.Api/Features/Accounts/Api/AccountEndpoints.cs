using Recipoke.Api.Features.Accounts.Application;
namespace Recipoke.Api.Features.Accounts.Api;
public static class AccountEndpoints
{
    public static void Map(WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization();
        admin.MapGet("/accounts", async (AdministratorAccounts operation, CancellationToken ct) => Results.Ok(await operation.ListAsync(ct))).Produces<IReadOnlyList<AccountResponse>>();
        admin.MapPut("/accounts/{id:guid}/status", async (Guid id, SetAccountStatusInput input, AdministratorAccounts operation, CancellationToken ct) =>
        { await operation.SetStatusAsync(id, input.IsActive, ct); return Results.NoContent(); }).Produces(204);
        admin.MapGet("/invitations", async (Invitations operation, CancellationToken ct) => Results.Ok(await operation.ListAsync(ct))).Produces<IReadOnlyList<InvitationResponse>>();
        admin.MapPost("/invitations", async (Invitations operation, CancellationToken ct) => Results.Ok(await operation.CreateAsync(ct))).Produces<CreatedInvitation>();
        admin.MapDelete("/invitations/{id:guid}", async (Guid id, Invitations operation, CancellationToken ct) =>
        { await operation.RevokeAsync(id, ct); return Results.NoContent(); }).Produces(204);
        var invitations = app.MapGroup("/api/invitations").RequireRateLimiting("invitation");
        invitations.MapPost("/validate", async (InvitationTokenInput input, Invitations operation, CancellationToken ct) => Results.Ok(await operation.ValidateAsync(input.Token, ct))).Produces<InvitationValidity>();
        invitations.MapPost("/redeem", async (RedeemInvitationInput input, Invitations operation, CancellationToken ct) =>
        { await operation.RedeemAsync(input, ct); return Results.NoContent(); }).Produces(204).ProducesValidationProblem();
    }
}
