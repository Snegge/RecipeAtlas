using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Recipoke.Api.Features.Shopping.Application;

namespace Recipoke.Api.Features.Shopping.Infrastructure;

public sealed class ShoppingNotifications(
    ShoppingConnections connections,
    IServiceScopeFactory scopes,
    IHubContext<ShoppingHub> hub,
    ILogger<ShoppingNotifications> logger) : IShoppingNotifications
{
    internal static async Task<bool> CanReadAsync(RecipeDb db, ShoppingConnections.Connection connection, Guid listId, CancellationToken ct) =>
        await db.Users.AnyAsync(user => user.Id == connection.UserId && user.IsActive && user.SecurityStamp == connection.SecurityStamp, ct) &&
        await ShoppingAccess.CanReadAsync(db, listId, connection.UserId, ct);

    public async Task NotifyAsync(Guid listId, CancellationToken ct)
    {
        try
        {
            // The caller already committed. A fresh scope avoids tracked/stale account or membership data.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RecipeDb>();
            foreach (var (id, connection) in connections.Watching(listId))
            {
                if (await CanReadAsync(db, connection, listId, deadline.Token))
                    await hub.Clients.Client(id).SendAsync("Changed", listId, deadline.Token);
                else
                {
                    connection.Lists.TryRemove(listId, out _);
                    await hub.Clients.Client(id).SendAsync("AccessLost", listId, deadline.Token);
                    if (!await db.Users.AnyAsync(x => x.Id == connection.UserId && x.IsActive &&
                        x.SecurityStamp == connection.SecurityStamp, deadline.Token))
                    {
                        connections.Remove(id);
                        connection.Abort();
                    }
                }
            }
        }
        catch (Exception exception)
        {
            // A missed invalidation is recovered by refresh; it must never turn a committed write into failure.
            logger.LogWarning(exception, "Shopping notification delivery failed; clients recover through refresh.");
        }
    }
}
