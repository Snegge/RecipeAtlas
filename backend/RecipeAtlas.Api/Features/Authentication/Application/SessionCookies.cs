namespace Recipoke.Api.Features.Authentication.Application;
public interface ISessionCookies
{
    Task SignInAsync(Guid userId);
    Task SignOutAsync();
}
