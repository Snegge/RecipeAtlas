using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Recipoke.Api.Infrastructure.Persistence;

// No commits/retries: application operations still own the complete write transaction.
public static class ConcurrencyWrites
{
    public static void Advance<TEntity>(PropertyEntry<TEntity, Guid> property, VersionPrecondition expected) where TEntity : class
    {
        property.OriginalValue = expected.Require();
        property.CurrentValue = Guid.NewGuid();
        property.IsModified = true;
    }

    public static async Task SaveAsync(RecipeDb db, VersionPrecondition expected,
        Func<Task> requireCurrentAccess, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            // Roll back child/image writes before inspecting current authorization with fresh state.
            var failed = db.Database.CurrentTransaction
                ?? throw new InvalidOperationException("Versioned writes require an application-owned transaction.");
            await failed.RollbackAsync(ct);
            await failed.DisposeAsync();
            db.ChangeTracker.Clear();
            await using var check = await WriteTransaction.BeginAsync(db, ct);
            await requireCurrentAccess();
            throw expected.Conflict();
        }
    }
}
