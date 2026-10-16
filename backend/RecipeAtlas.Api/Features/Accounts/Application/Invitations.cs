using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Recipoke.Domain.Accounts;
namespace Recipoke.Api.Features.Accounts.Application;

public sealed class Invitations(RecipeDb db, AccountAccess access, UserManager<AppUser> users, TimeProvider clock, IOptions<AccountOptions> options)
{
    private string Status(AccountInvitation invitation) => InvitationRules.Status(clock.GetUtcNow(), invitation.ExpiresAtUtc, invitation.RevokedAtUtc, invitation.ConsumedAtUtc);
    private InvitationResponse Response(AccountInvitation invitation) => new(invitation.Id, invitation.CreatedAtUtc, invitation.ExpiresAtUtc, Status(invitation));
    private static string Hash(string? token)
    {
        if (token is null || !Regex.IsMatch(token, @"^[A-Za-z0-9_-]{43}$")) throw new OperationFailure(400, "This invitation is invalid.", "invalid_invitation");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
    public async Task<CreatedInvitation> CreateAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var issuer = await access.RequireAdministratorAsync(ct);
        var settings = options.Value;
        if (!Uri.TryCreate(settings.PublicAppUrl, UriKind.Absolute, out var publicUrl) || publicUrl.UserInfo.Length != 0 ||
            publicUrl.Query.Length != 0 || publicUrl.Fragment.Length != 0 ||
            !(publicUrl.Scheme == "https" || publicUrl.Scheme == "http" && publicUrl.IsLoopback) || settings.InvitationHours is < 1 or > 720)
            throw new OperationFailure(503, "Configure Accounts:PublicAppUrl and InvitationHours on the server.", "invitations_not_configured");
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = clock.GetUtcNow();
        var invitation = new AccountInvitation { TokenHash = Hash(token), IssuedByUserId = issuer.Id,
            CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(settings.InvitationHours) };
        db.Invitations.Add(invitation); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        // A URL fragment is never sent in HTTP requests or Referer headers.
        return new(Response(invitation), publicUrl.AbsoluteUri.TrimEnd('/') + "/invite#token=" + token);
    }
    public async Task<IReadOnlyList<InvitationResponse>> ListAsync(CancellationToken ct)
    {
        await access.RequireAdministratorAsync(ct);
        return (await db.Invitations.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct)).Select(Response).ToArray();
    }
    public async Task RevokeAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await access.RequireAdministratorAsync(ct);
        var invitation = await db.Invitations.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new OperationFailure(404, "Invitation not found.");
        if (Status(invitation) != "Pending") throw new OperationFailure(409, "Only pending invitations can be revoked.", "invitation_not_pending");
        invitation.RevokedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task<InvitationValidity> ValidateAsync(string? token, CancellationToken ct)
    {
        var hash = Hash(token);
        var invitation = await db.Invitations.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        return invitation is null ? new("Invalid", null) : new(Status(invitation), invitation.ExpiresAtUtc);
    }
    public async Task RedeemAsync(RedeemInvitationInput input, CancellationToken ct)
    {
        var hash = Hash(input.Token);
        var profile = AccountValidation.Profile(input.Username, input.DisplayName); AccountValidation.Password(input.Password);
        // SQLite Serializable starts BEGIN IMMEDIATE: redemption/revocation are ordered before checking state.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var invitation = await db.Invitations.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (invitation is null || Status(invitation) != "Pending")
            throw new OperationFailure(400, "This invitation is invalid, expired, revoked or already used.", "invalid_invitation");
        var user = new AppUser { Id = Guid.NewGuid(), UserName = profile.Username, DisplayName = profile.DisplayName, IsActive = true };
        AccountValidation.RequireSuccess(await users.CreateAsync(user, input.Password!));
        db.Cookbooks.Add(new Cookbook { OwnerUserId = user.Id, Name = "Personal cookbook" });
        invitation.ConsumedAtUtc = clock.GetUtcNow(); invitation.ConsumedByUserId = user.Id;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
