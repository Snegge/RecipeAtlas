using Microsoft.AspNetCore.Http.Connections;
using Recipoke.Api.Features.Shopping.Application;

namespace Recipoke.Api.Features.Shopping.Infrastructure;

public static class ShoppingRealtime
{
    public static IServiceCollection AddShoppingRealtime(this IServiceCollection services)
    {
        services.AddSignalR(options => options.MaximumReceiveMessageSize = 4096);
        services.AddSingleton<ShoppingConnections>();
        services.AddSingleton<IShoppingNotifications, ShoppingNotifications>();
        return services;
    }

    public static void MapShoppingRealtime(this WebApplication app) =>
        app.MapHub<ShoppingHub>("/api/shopping-events", options =>
        {
            // Browser LongPolling supports the existing write-protection header on all hub POSTs.
            options.Transports = HttpTransportType.LongPolling;
            options.CloseOnAuthenticationExpiration = true;
        });
}
