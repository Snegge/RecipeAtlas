namespace Recipoke.Domain.Shopping;

public sealed class ShoppingItem
{
    public Guid Id { get; set; }
    public Guid ListId { get; set; }
    public string Text { get; set; } = "";
    public bool Purchased { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public long Position { get; set; }
    public bool Deleted { get; set; }
    public Guid? DeletedByUserId { get; set; }
    public Guid? DeletionOperationId { get; set; }
    public DateTimeOffset? UndoUntilUtc { get; set; }
}

public sealed class ShoppingReservation
{
    public Guid ItemId { get; set; }
    public Guid ListId { get; set; }
    public Guid UserId { get; set; }
    public Guid Token { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

// Remember consumed edit-session IDs after release/expiry; delayed acquire requests cannot revive them.
public sealed class ShoppingReservationToken
{
    public Guid ItemId { get; set; }
    public Guid Token { get; set; }
}

public sealed class ShoppingReceipt
{
    public Guid ListId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid OperationId { get; set; }
    public string Operation { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string ResultJson { get; set; } = "";
}
