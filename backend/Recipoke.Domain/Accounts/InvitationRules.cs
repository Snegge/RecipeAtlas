namespace Recipoke.Domain.Accounts;

public static class InvitationRules
{
    // Terminal states take precedence over expiry. Reads never consume an invitation.
    public static string Status(DateTimeOffset now, DateTimeOffset expires, DateTimeOffset? revoked, DateTimeOffset? consumed) =>
        consumed is not null ? "Consumed" : revoked is not null ? "Revoked" : now >= expires ? "Expired" : "Pending";
}
