using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace Recipoke.Api.Features.Authentication.Infrastructure;

public sealed class IdentitySessions(UserManager<AppUser> users, SignInManager<AppUser> signIn) : ISessionCookies
{
    public async Task SignInAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive) throw new OperationFailure(401, "Sign in to continue.");
        await signIn.SignInAsync(user, isPersistent: true);
    }
    public Task SignOutAsync() => signIn.SignOutAsync();
}
public static class ActiveSessionValidation
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var signIn = context.HttpContext.RequestServices.GetRequiredService<SignInManager<AppUser>>();
        var user = await signIn.ValidateSecurityStampAsync(context.Principal);
        if (user is null || !user.IsActive)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    }
}
