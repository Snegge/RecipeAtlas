using Recipoke.Api.Common.Application;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

internal static class RecipeOperationTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "recipoke-operations-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var fail = new FailAfterFirstWrite();
        var options = new DbContextOptionsBuilder<RecipeDb>().UseSqlite("Data Source=" + Path.Combine(directory, "test.db"))
            .AddInterceptors(fail).Options;
        try
        {
            await using var db = new RecipeDb(options);
            await db.Database.MigrateAsync();
            var owner = await db.Users.SingleAsync(x => x.Id == LegacySetup.UserId); owner.IsActive = true;
            await db.SaveChangesAsync();
            var current = new TestCurrentUser(owner.Id);
            var access = new CookbookAccess(db, new AccountAccess(db, current));
            var cookbookId = LegacySetup.CookbookId;
            RecipeInput Input(string title) => new(title, null, null, 2,
                [new("Eggs", "3 bis 4", "piece", null), new("Salt", "", "toTaste", null)], ["Mix", "Cook"]);
            var create = new CreateRecipe(db, access);
            check((await create.ExecuteAsync(cookbookId, Input(""), default)).Errors.ContainsKey("title") && !await db.Recipes.AnyAsync(), "Invalid create never persists");
            var created = (await create.ExecuteAsync(cookbookId, Input("Original"), default)).Recipe!;
            var id = created.Id;
            check(created.Ingredients[0].Quantity == "3-4" && created.Steps.SequenceEqual(["Mix", "Cook"]), "Create normalises quantities and preserves order");
            var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 };
            var replace = new ReplaceRecipeImage(db, access);
            string Version() => db.Recipes.AsNoTracking().Single(x => x.Id == id).Version.ToString();
            VersionPrecondition Header() => new("\"" + Version() + "\"", true);
            check((await replace.ExecuteAsync(cookbookId, id, new MemoryStream(bytes), bytes.Length, Header(), default)).Error is null, "Replace image persists");
            check((await new GetRecipeImage(db, access).ExecuteAsync(cookbookId, id, default))!.Bytes.SequenceEqual(bytes), "Image query returns exact bytes");
            check((await replace.ExecuteAsync(cookbookId, id, new MemoryStream([1, 2]), 2, Header(), default)).Error == ImageReplacementError.UnsupportedType, "Invalid image rejected");
            check((await new GetRecipeImage(db, access).ExecuteAsync(cookbookId, id, default))!.Bytes.SequenceEqual(bytes), "Invalid image leaves old image intact");
            check((await replace.ExecuteAsync(cookbookId, Guid.NewGuid(), Stream.Null, ReplaceRecipeImage.MaxImageBytes + 1, Header(), default)).Error == ImageReplacementError.TooLarge, "Size precedes existence check");
            var update = new UpdateRecipe(db, access);
            var changed = (await update.ExecuteAsync(cookbookId, id, Input("Updated") with { Steps = ["Second", "First"], ExpectedVersion = Version() }, default)).Recipe!;
            check(changed.ImageUrl == $"/api/cookbooks/{cookbookId}/recipes/{id}/image" && (await new GetRecipeImage(db, access).ExecuteAsync(cookbookId, id, default))!.Bytes.SequenceEqual(bytes), "Text update preserves image");
            check(changed.Steps.SequenceEqual(["Second", "First"]), "Update replaces ordered steps");
            try { await update.ExecuteAsync(cookbookId, Guid.NewGuid(), Input("Missing"), default); throw new Exception("Missing update accepted"); }
            catch (OperationFailure e) { check(e.StatusCode == 404 && e.Code == "resource_unavailable", "Update missing resource"); }
            // Fail after EF has begun the multi-statement replacement. Read with a fresh context.
            fail.Enabled = true;
            try { await update.ExecuteAsync(cookbookId, id, Input("Must roll back") with { ExpectedVersion = Version() }, default); throw new Exception("Expected injected write failure"); }
            catch (DbUpdateException) { }
            check(fail.WritesBeforeFailure > 0, "Rollback test fails only after a real write executed");
            fail.Enabled = false;
            db.ChangeTracker.Clear();
            await using (var verify = new RecipeDb(options))
            {
                var persisted = await new GetRecipe(verify, new CookbookAccess(verify, new AccountAccess(verify, current))).ExecuteAsync(cookbookId, id, default);
                check(persisted!.Title == "Updated" && persisted.Steps.SequenceEqual(["Second", "First"]) && persisted.Ingredients.Count == 2,
                    "Single SaveChanges rolls back recipe and child replacement together");
            }
            check((await new ListRecipes(db, access).ExecuteAsync(cookbookId, "Updated", 1, 12, default)).Page!.Total == 1, "Projected list query");
            check((await new ListRecipes(db, access).ExecuteAsync(cookbookId, null, 0, 12, default)).Error is not null, "List prerequisites validated");
            check((await new RemoveRecipeImage(db, access).ExecuteAsync(cookbookId, id, Header(), default)).ImageUrl is null, "Remove image");
            check((await new GetRecipe(db, access).ExecuteAsync(cookbookId, id, default))!.ImageUrl is null, "Image removal reflected in recipe");
            await replace.ExecuteAsync(cookbookId, id, new MemoryStream(bytes), bytes.Length, Header(), default);
            check(await new DeleteRecipe(db, access).ExecuteAsync(cookbookId, id, Header(), default), "Delete existing recipe");
            check(!await db.Images.AnyAsync() && !await db.Set<Ingredient>().AnyAsync() && !await db.Set<RecipeStep>().AnyAsync(), "SQLite cascades children and image");
            try { await new DeleteRecipe(db, access).ExecuteAsync(cookbookId, id, new(Guid.NewGuid().ToString()), default); throw new Exception("Missing delete accepted"); }
            catch (OperationFailure e) { check(e.StatusCode == 404 && e.Code == "resource_unavailable", "Delete missing recipe"); }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }
    private sealed class FailAfterFirstWrite : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public int WritesBeforeFailure { get; private set; }
        private static bool IsWrite(DbCommand command) => new[] { "INSERT", "UPDATE", "DELETE" }.Any(word => command.CommandText.TrimStart().StartsWith(word, StringComparison.Ordinal));
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && IsWrite(command) && WritesBeforeFailure > 0) throw new InvalidOperationException("Injected write failure after a successful statement");
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Enabled && IsWrite(command)) WritesBeforeFailure++;
            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }
}
