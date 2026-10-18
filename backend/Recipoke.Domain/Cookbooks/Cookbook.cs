namespace Recipoke.Domain.Cookbooks;

public enum CookbookRole { Owner, Editor, Viewer }
public enum CookbookPermission { Read, Write, Manage }
public sealed class Cookbook
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public CookbookImage? Image { get; set; }
    public Guid OwnerUserId { get; set; }
}
public sealed class CookbookMembership
{
    public Guid CookbookId { get; set; }
    public Guid UserId { get; set; }
    public CookbookRole Role { get; set; }
}
public static class CookbookPermissions
{
    public static bool Allows(CookbookRole role, CookbookPermission permission) => permission switch
    {
        CookbookPermission.Read => role is CookbookRole.Owner or CookbookRole.Editor or CookbookRole.Viewer,
        CookbookPermission.Write => role is CookbookRole.Owner or CookbookRole.Editor,
        CookbookPermission.Manage => role == CookbookRole.Owner,
        _ => false
    };
}
