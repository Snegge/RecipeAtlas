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
            RecipeInput Input(string title) => new(title, null, null, 2,
                [new("Eggs", "3 bis 4", "piece", null), new("Salt", "", "toTaste", null)], ["Mix", "Cook"]);
            var create = new CreateRecipe(db);
            check((await create.ExecuteAsync(Input(""), default)).Errors.ContainsKey("title") && !await db.Recipes.AnyAsync(), "Invalid create never persists");
            var created = (await create.ExecuteAsync(Input("Original"), default)).Recipe!;
            var id = created.Id;
            check(created.Ingredients[0].Quantity == "3-4" && created.Steps.SequenceEqual(["Mix", "Cook"]), "Create normalises quantities and preserves order");
            var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 };
            var replace = new ReplaceRecipeImage(db);
            check(await replace.ExecuteAsync(id, new MemoryStream(bytes), bytes.Length, default) is null, "Replace image persists");
            check((await new GetRecipeImage(db).ExecuteAsync(id, default))!.Bytes.SequenceEqual(bytes), "Image query returns exact bytes");
            check(await replace.ExecuteAsync(id, new MemoryStream([1, 2]), 2, default) == ImageReplacementError.UnsupportedType, "Invalid image rejected");
            check((await new GetRecipeImage(db).ExecuteAsync(id, default))!.Bytes.SequenceEqual(bytes), "Invalid image leaves old image intact");
            check(await replace.ExecuteAsync(Guid.NewGuid(), Stream.Null, ReplaceRecipeImage.MaxImageBytes + 1, default) == ImageReplacementError.TooLarge, "Size precedes existence check");
            var update = new UpdateRecipe(db);
            var changed = (await update.ExecuteAsync(id, Input("Updated") with { Steps = ["Second", "First"] }, default)).Recipe!;
            check(changed.ImageUrl == $"/api/recipes/{id}/image" && (await new GetRecipeImage(db).ExecuteAsync(id, default))!.Bytes.SequenceEqual(bytes), "Text update preserves image");
            check(changed.Steps.SequenceEqual(["Second", "First"]), "Update replaces ordered steps");
            check((await update.ExecuteAsync(Guid.NewGuid(), Input("Missing"), default)).Recipe is null, "Update missing resource");
            // Fail after EF has begun the multi-statement replacement. Read with a fresh context.
            fail.Enabled = true;
            try { await update.ExecuteAsync(id, Input("Must roll back"), default); throw new Exception("Expected injected write failure"); }
            catch (DbUpdateException) { }
            check(fail.WritesBeforeFailure > 0, "Rollback test fails only after a real write executed");
            fail.Enabled = false;
            db.ChangeTracker.Clear();
            await using (var verify = new RecipeDb(options))
            {
                var persisted = await new GetRecipe(verify).ExecuteAsync(id, default);
                check(persisted!.Title == "Updated" && persisted.Steps.SequenceEqual(["Second", "First"]) && persisted.Ingredients.Count == 2,
                    "Single SaveChanges rolls back recipe and child replacement together");
            }
            check((await new ListRecipes(db).ExecuteAsync("Updated", 1, 12, default)).Page!.Total == 1, "Projected list query");
            check((await new ListRecipes(db).ExecuteAsync(null, 0, 12, default)).Error is not null, "List prerequisites validated");
            check(await new RemoveRecipeImage(db).ExecuteAsync(id, default), "Remove image");
            check((await new GetRecipe(db).ExecuteAsync(id, default))!.ImageUrl is null, "Image removal reflected in recipe");
            await replace.ExecuteAsync(id, new MemoryStream(bytes), bytes.Length, default);
            check(await new DeleteRecipe(db).ExecuteAsync(id, default), "Delete existing recipe");
            check(!await db.Images.AnyAsync() && !await db.Set<Ingredient>().AnyAsync() && !await db.Set<RecipeStep>().AnyAsync(), "SQLite cascades children and image");
            check(!await new DeleteRecipe(db).ExecuteAsync(id, default), "Delete missing recipe");
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
