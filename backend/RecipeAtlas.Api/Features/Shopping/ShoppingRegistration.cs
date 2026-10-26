using Recipoke.Api.Features.Shopping.Application;

namespace Recipoke.Api.Features.Shopping;

public static class ShoppingRegistration
{
    public static IServiceCollection AddShoppingOperations(this IServiceCollection services) =>
        services.AddScoped<ShoppingAccess>().AddScoped<ShoppingOperations>();
}
