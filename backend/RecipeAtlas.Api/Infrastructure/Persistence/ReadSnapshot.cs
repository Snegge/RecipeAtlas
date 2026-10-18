using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Recipoke.Api.Infrastructure.Persistence;

// Split queries must see one snapshot, so a representation cannot combine old children and a new version.
// Deferred SQLite reads do not acquire the BEGIN IMMEDIATE writer reservation used by write operations.
public sealed class ReadSnapshot : IAsyncDisposable
{
    private readonly RecipeDb db;
    private readonly SqliteTransaction? sqlite;
    private readonly IDbContextTransaction? ef;
    private ReadSnapshot(RecipeDb db, SqliteTransaction? sqlite = null, IDbContextTransaction? ef = null)
    { this.db = db; this.sqlite = sqlite; this.ef = ef; }

    public static async Task<ReadSnapshot> BeginAsync(RecipeDb db, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return new(db);
        await db.Database.OpenConnectionAsync(ct);
        var sqlite = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: true);
        try { return new(db, sqlite, await db.Database.UseTransactionAsync(sqlite, ct)); }
        catch { await sqlite.DisposeAsync(); await db.Database.CloseConnectionAsync(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        if (sqlite is null) return;
        if (ef is not null) await ef.DisposeAsync();
        await sqlite.DisposeAsync();
        await db.Database.CloseConnectionAsync();
    }
}
