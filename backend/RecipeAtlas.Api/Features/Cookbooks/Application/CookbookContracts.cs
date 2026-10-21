using System.Text.Json.Serialization;

namespace Recipoke.Api.Features.Cookbooks.Application;

public sealed record CookbookResponse(Guid Id, string Name, string Role, Guid Version, string? Description = null, string? ImageUrl = null, int RecipeCount = 0);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CookbookNameInput(string? Name, string? Description = null, string? ExpectedVersion = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateCookbookInput(string? Name, string? Description = null, IReadOnlyList<AddMemberInput>? Members = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AddMemberInput(string? Username, string? Role, string? ExpectedVersion = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MemberRoleInput(string? Role, string? ExpectedVersion = null);
public sealed record MemberResponse(Guid UserId, string Username, string DisplayName, string Role, bool IsActive, string? AvatarUrl = null, bool IsPending = false);
public sealed record CookbookInvitationResponse(Guid Id, Guid CookbookId, string CookbookName, string Role, string OwnerDisplayName, string? OwnerAvatarUrl, string? ImageUrl, Guid CookbookVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RespondToCookbookInvitationInput([property: JsonRequired] bool Accept, string? ExpectedVersion = null);
public sealed record CookbookMembersResponse(CookbookResponse Cookbook, IReadOnlyList<MemberResponse> Members);
public sealed record VersionResponse(Guid Version);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExpectedVersionInput(string? ExpectedVersion);
public static class CookbookValidation
{
    public static string Name(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100)
            throw new OperationFailure(400, "Use a cookbook name with 1–100 characters.");
        return name;
    }

    public static string? Description(string? text)
    {
        text = text?.Trim();
        if (text?.Length > 2000)
            throw new OperationFailure(400, "Use a description of at most 2000 characters.");
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public static CookbookRole MemberRole(string? role) => role switch
    {
        "Editor" => CookbookRole.Editor,
        "Viewer" => CookbookRole.Viewer,
        _ => throw new OperationFailure(400, "Choose Editor or Viewer.")
    };
}
