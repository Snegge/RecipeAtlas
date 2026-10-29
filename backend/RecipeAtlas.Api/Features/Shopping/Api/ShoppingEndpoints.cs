using Microsoft.AspNetCore.Mvc;
using Recipoke.Api.Features.Shopping.Application;

namespace Recipoke.Api.Features.Shopping.Api;

public static class ShoppingEndpoints
{
    private static VersionPrecondition IfMatch(HttpRequest request) => new(request.Headers.IfMatch.ToString(), true);
    private static Guid OperationId(HttpRequest request) => Guid.TryParseExact(request.Headers["X-Operation-Id"].ToString(), "D", out var id) ? id : Guid.Empty;

    public static void Map(WebApplication app)
    {
        var lists = app.MapGroup("/api/shopping-lists").RequireAuthorization();
        lists.MapGet("/", async (ShoppingOperations operations, CancellationToken ct) => Results.Ok(await operations.ListAsync(ct))).Produces<IReadOnlyList<ShoppingListSummary>>();
        lists.MapPost("/", async (CreateShoppingListInput input, ShoppingOperations operations, CancellationToken ct) => Results.Ok(await operations.CreateAsync(input, ct))).Produces<ShoppingMutationResponse>();
        lists.MapGet("/{listId:guid}", async (Guid listId, ShoppingOperations operations, CancellationToken ct) => Results.Ok(await operations.GetAsync(listId, ct))).Produces<ShoppingListResponse>();
        lists.MapPut("/{listId:guid}", async (Guid listId, RenameShoppingListInput input, ShoppingOperations operations, CancellationToken ct) => Results.Ok(await operations.RenameAsync(listId, input, ct))).Produces<ShoppingMutationResponse>();
        lists.MapDelete("/{listId:guid}", async (Guid listId, HttpRequest request, ShoppingOperations operations, CancellationToken ct) =>
        {
            await operations.DeleteAsync(listId, IfMatch(request), OperationId(request), ct);
            return Results.NoContent();
        }).Produces(204);
        lists.MapPost("/{listId:guid}/invitations", async (Guid listId, InviteShoppingMemberInput input, ShoppingOperations operations, CancellationToken ct) => Results.Ok(await operations.InviteAsync(listId, input, ct))).Produces<ShoppingMutationResponse>();
        lists.MapDelete("/{listId:guid}/invitations/{id:guid}", async (Guid listId, Guid id, HttpRequest request, ShoppingOperations operations, CancellationToken ct) => Results.Ok(await operations.CancelInvitationAsync(listId, id, IfMatch(request), OperationId(request), ct))).Produces<ShoppingMutationResponse>();
        lists.MapPost("/{listId:guid}/invitations/{id:guid}/respond", async (Guid listId, Guid id, RespondShoppingInvitationInput input, ShoppingOperations operations, CancellationToken ct) => Results.Ok(await operations.RespondAsync(listId, id, input, ct))).Produces<ShoppingMutationResponse>();
        lists.MapDelete("/{listId:guid}/members/{userId:guid}", async (Guid listId, Guid userId, HttpRequest request, ShoppingOperations operations, CancellationToken ct) => Results.Ok(await operations.RemoveMemberAsync(listId, userId, IfMatch(request), OperationId(request), ct))).Produces<ShoppingMutationResponse>();
        lists.MapPost("/{listId:guid}/leave", async (Guid listId, ShoppingVersionInput input, ShoppingOperations operations, CancellationToken ct) => Results.Ok(await operations.LeaveAsync(listId, input, ct))).Produces<ShoppingMutationResponse>();
        lists.MapPost("/{listId:guid}/transfer", async (Guid listId, TransferShoppingListInput input, ShoppingOperations operations, CancellationToken ct) => Results.Ok(await operations.TransferAsync(listId, input, ct))).Produces<ShoppingMutationResponse>();
        lists.MapPost("/{listId:guid}/items", async (Guid listId, AddShoppingItemInput input, ShoppingOperations operations, CancellationToken ct) => Results.Ok(await operations.AddItemAsync(listId, input, ct))).Produces<ShoppingMutationResponse>();
        lists.MapPut("/{listId:guid}/items/{id:guid}", async (Guid listId, Guid id, SaveShoppingItemInput input, ShoppingOperations operations, CancellationToken ct) => Results.Ok(await operations.SaveItemAsync(listId, id, input, ct))).Produces<ShoppingMutationResponse>();
        lists.MapPut("/{listId:guid}/items/{id:guid}/purchased", async (Guid listId, Guid id, PurchasedShoppingItemInput input, ShoppingOperations operations, CancellationToken ct) => Results.Ok(await operations.SetPurchasedAsync(listId, id, input, ct))).Produces<ShoppingMutationResponse>();
        lists.MapDelete("/{listId:guid}/items/{id:guid}", async (Guid listId, Guid id, HttpRequest request, ShoppingOperations operations, CancellationToken ct) => Results.Ok(await operations.DeleteItemAsync(listId, id, IfMatch(request), OperationId(request), ct))).Produces<ShoppingMutationResponse>();
        lists.MapPost("/{listId:guid}/items/{id:guid}/restore", async (Guid listId, Guid id, RestoreShoppingItemInput input, ShoppingOperations operations, CancellationToken ct) => Results.Ok(await operations.RestoreItemAsync(listId, id, input, ct))).Produces<ShoppingMutationResponse>();
        lists.MapPost("/{listId:guid}/items/{id:guid}/reservation", async (Guid listId, Guid id, AcquireShoppingReservationInput input, ShoppingOperations operations, CancellationToken ct) => Results.Ok(await operations.AcquireAsync(listId, id, input, ct))).Produces<ShoppingReservationResponse>();
        lists.MapDelete("/{listId:guid}/items/{id:guid}/reservation", async (Guid listId, Guid id, [FromBody] ShoppingReservationInput input, ShoppingOperations operations, CancellationToken ct) =>
        {
            await operations.ReleaseAsync(listId, id, input.Token, ct);
            return Results.NoContent();
        }).Produces(204);
        app.MapGet("/api/shopping-list-invitations", async (ShoppingOperations operations, CancellationToken ct) => Results.Ok(await operations.InboxAsync(ct)))
            .RequireAuthorization().Produces<IReadOnlyList<ShoppingInvitationResponse>>();
    }
}
