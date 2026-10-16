using Microsoft.AspNetCore.Identity;
namespace Recipoke.Api.Infrastructure.Persistence;

// Framework-specific identity storage never crosses into Domain or HTTP responses.
public sealed class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool IsAdministrator { get; set; }
}
public sealed class AccountInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TokenHash { get; set; } = "";
    public Guid IssuedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public Guid? ConsumedByUserId { get; set; }
}
public sealed class AccountSetup
{
    public int Id { get; set; } = 1;
    public DateTimeOffset CompletedAtUtc { get; set; }
}
public static class LegacySetup
{
    public static readonly Guid UserId = Guid.Parse("719ead20-283b-4ad3-995e-fd1e9b09b401");
    public static readonly Guid CookbookId = Guid.Parse("719ead20-283b-4ad3-995e-fd1e9b09b402");
}
