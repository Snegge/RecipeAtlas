using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Recipoke.Api.Infrastructure.Persistence;

public static class WriteTransaction
{
    public static async Task<IDbContextTransaction> BeginAsync(RecipeDb db, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            // shortcut: serialize prototype writes like SQLite; revisit lock granularity before scaling write throughput.
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '15s'", ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(724963018)", ct);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }
}
