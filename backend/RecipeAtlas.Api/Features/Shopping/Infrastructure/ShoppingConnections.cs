using System.Collections.Concurrent;

namespace Recipoke.Api.Features.Shopping.Infrastructure;

// Connection routing only; every subscription and delivery checks current database access.
public sealed class ShoppingConnections
{
    public sealed record Connection(Guid UserId, string SecurityStamp, Action Abort)
    {
        public ConcurrentDictionary<Guid, byte> Lists { get; } = new();
    }

    private readonly ConcurrentDictionary<string, Connection> connections = new();
    public void Add(string id, Connection connection) => connections[id] = connection;
    public Connection? Get(string id) => connections.GetValueOrDefault(id);
    public void Remove(string id) => connections.TryRemove(id, out _);
    public KeyValuePair<string, Connection>[] Watching(Guid listId) =>
        connections.Where(x => x.Value.Lists.ContainsKey(listId)).ToArray();
}
