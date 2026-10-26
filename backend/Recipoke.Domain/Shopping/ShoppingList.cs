namespace Recipoke.Domain.Shopping;

public enum ShoppingRole { Owner, Member }

public sealed class ShoppingList
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid OwnerUserId { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class ShoppingMembership
{
    public Guid ListId { get; set; }
    public Guid UserId { get; set; }
}

public sealed class ShoppingInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListId { get; set; }
    public Guid UserId { get; set; }
}

// Consumed client IDs survive content deletion so delayed creation cannot resurrect a list.
public sealed class ShoppingListTombstone
{
    public Guid Id { get; set; }
}
