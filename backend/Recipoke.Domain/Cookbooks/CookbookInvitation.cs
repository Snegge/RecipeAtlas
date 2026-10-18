namespace Recipoke.Domain.Cookbooks;

// Only pending invitations are stored. Accept/decline removes this row atomically.
public sealed class CookbookInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CookbookId { get; set; }
    public Guid UserId { get; set; }
    public CookbookRole Role { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Cookbook Cookbook { get; set; } = null!;
}
