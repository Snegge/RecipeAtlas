using System.Text.Json.Serialization;

namespace Recipoke.Api.Features.Shopping.Application;

public sealed record ShoppingItemResponse(Guid Id, string Text, bool Purchased, Guid Version, long Position,
    string? EditingBy, DateTimeOffset? ReservationExpiresAtUtc);
public sealed record ShoppingListSummary(Guid Id, string Name, string Role, Guid Version,
    IReadOnlyList<string> Preview, int ActiveCount, int PurchasedCount);
public sealed record ShoppingMemberResponse(Guid UserId, string Username, string DisplayName, string Role);
public sealed record ShoppingPendingInvitation(Guid Id, Guid UserId, string Username, string DisplayName);
public sealed record ShoppingListResponse(Guid Id, string Name, string Role, Guid Version,
    IReadOnlyList<ShoppingItemResponse> Items, IReadOnlyList<ShoppingMemberResponse> Members, IReadOnlyList<ShoppingPendingInvitation> Invitations);
public sealed record ShoppingInvitationResponse(Guid Id, Guid ListId, string ListName, string OwnerDisplayName, Guid ListVersion);
public sealed record ShoppingMutationResponse(Guid ListId, Guid? Version, ShoppingItemResponse? Item, DateTimeOffset? UndoUntilUtc);
public sealed record ShoppingReservationResponse(Guid Token, DateTimeOffset ExpiresAtUtc);

public sealed record CreateShoppingListInput(Guid ListId, string? Name, Guid OperationId);
public sealed record RenameShoppingListInput(string? Name, string? ExpectedVersion, Guid OperationId);
public sealed record ShoppingVersionInput(string? ExpectedVersion, Guid OperationId);
public sealed record InviteShoppingMemberInput(string? Username, string? ExpectedVersion, Guid OperationId);
public sealed record RespondShoppingInvitationInput([property: JsonRequired] bool Accept, string? ExpectedVersion, Guid OperationId);
public sealed record TransferShoppingListInput(Guid NewOwnerUserId, string? ExpectedVersion, Guid OperationId);
public sealed record AddShoppingItemInput(Guid Id, string? Text, Guid OperationId);
public sealed record SaveShoppingItemInput(string? Text, string? ExpectedVersion, Guid Token, Guid OperationId);
public sealed record PurchasedShoppingItemInput([property: JsonRequired] bool Purchased, string? ExpectedVersion, Guid OperationId);
public sealed record RestoreShoppingItemInput(Guid DeletionOperationId, Guid OperationId);
public sealed record AcquireShoppingReservationInput(Guid Token, string? ExpectedVersion);
public sealed record ShoppingReservationInput(Guid Token);

public interface IShoppingNotifications
{
    Task NotifyAsync(Guid listId, CancellationToken ct);
}
