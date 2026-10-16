using System.Text.Json.Serialization;
namespace Recipoke.Api.Features.Accounts.Application;
public sealed record CurrentUserResponse(Guid Id, string Username, string DisplayName, bool IsAdministrator);
public sealed record AccountResponse(Guid Id, string Username, string DisplayName, bool IsActive, bool IsAdministrator);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetAccountStatusInput([property: JsonRequired] bool IsActive);
public sealed record InvitationResponse(Guid Id, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc, string Status);
public sealed record CreatedInvitation(InvitationResponse Invitation, string Link);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InvitationTokenInput(string? Token);
public sealed record InvitationValidity(string Status, DateTimeOffset? ExpiresAtUtc);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RedeemInvitationInput(string? Token, string? Username, string? DisplayName, string? Password);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChangePasswordInput(string? CurrentPassword, string? NewPassword);
public sealed class AccountOptions
{
    public string PublicAppUrl { get; set; } = "";
    public int InvitationHours { get; set; } = 72;
}
