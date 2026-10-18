internal sealed class TestCurrentUser(Guid id) : ICurrentUser { public Guid? UserId => id; }
