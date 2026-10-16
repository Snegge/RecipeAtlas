using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
namespace Recipoke.Api.Features.Accounts.Application;

public static class AccountValidation
{
    public static (string Username, string DisplayName) Profile(string? username, string? displayName)
    {
        username = username?.Trim(); displayName = displayName?.Trim();
        var errors = new Dictionary<string, string[]>();
        if (username is null || !Regex.IsMatch(username, @"^[A-Za-z0-9][A-Za-z0-9._-]{2,31}$"))
            errors["username"] = ["Use 3–32 letters, digits, dots, hyphens or underscores; start with a letter or digit."];
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 100) errors["displayName"] = ["Use a display name with 1–100 characters."];
        if (errors.Count > 0) throw new OperationFailure(400, "Check your account details.", errors: errors);
        return (username!, displayName!);
    }
    public static void Password(string? password)
    {
        if (password is null || password.Length is < 12 or > 256)
            throw new OperationFailure(400, "Use a password with 12–256 characters.", errors: new() { ["password"] = ["Use a password with 12–256 characters."] });
    }
    public static void RequireSuccess(IdentityResult result)
    {
        if (!result.Succeeded) throw new OperationFailure(400, "Check your account details.", errors:
            result.Errors.GroupBy(x => x.Code.StartsWith("Duplicate") || x.Code.Contains("UserName") ? "username" : "password")
                .ToDictionary(g => g.Key, g => g.Select(x => x.Description).ToArray()));
    }
    public static CurrentUserResponse Response(AppUser user) => new(user.Id, user.UserName!, user.DisplayName, user.IsAdministrator);
}
