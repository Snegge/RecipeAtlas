using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Recipoke.Api.Features.Shopping.Infrastructure;

[Authorize]
public sealed class ShoppingHub(RecipeDb db, ShoppingConnections connections, IOptions<IdentityOptions> identity) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var stamp = Context.User?.FindFirst(identity.Value.ClaimsIdentity.SecurityStampClaimType)?.Value;
        if (!Guid.TryParse(Context.UserIdentifier, out var userId) || string.IsNullOrEmpty(stamp) ||
            !await db.Users.AnyAsync(x => x.Id == userId && x.IsActive && x.SecurityStamp == stamp, Context.ConnectionAborted))
        {
            Context.Abort();
            throw new HubException("Sign in to continue.");
        }
        connections.Add(Context.ConnectionId, new(userId, stamp, Context.Abort));
        await base.OnConnectedAsync();
    }

    public async Task Watch(Guid listId)
    {
        var connection = connections.Get(Context.ConnectionId);
        if (connection is null || !await ShoppingNotifications.CanReadAsync(db, connection, listId, Context.ConnectionAborted))
            throw new HubException("This shopping list is unavailable.");
        // One detail page needs one subscription; bounded multi-list overview subscriptions are allowed.
        if (connection.Lists.Count >= 100 && !connection.Lists.ContainsKey(listId))
            throw new HubException("Too many open shopping lists.");
        connection.Lists[listId] = 0;
    }

    public Task Unwatch(Guid listId)
    {
        connections.Get(Context.ConnectionId)?.Lists.TryRemove(listId, out _);
        return Task.CompletedTask;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
