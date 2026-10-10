using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RecipeAtlas.Api;
using RecipeAtlas.Api.Features.RecipeImport;

if (args.FirstOrDefault() == "--process-fixture")
{
    switch (args[1])
    {
        case "argument": Console.Write(args[2]); break;
        case "output": Console.Write(new string('x', 2 * 1024 * 1024 + 1)); break;
        case "error": Console.Error.Write(new string('x', 64 * 1024 + 1)); break;
        case "wait":
            File.WriteAllText(args[2], Environment.ProcessId.ToString());
            await Task.Delay(Timeout.InfiniteTimeSpan); break;
    }
    return;
}
if (args.FirstOrDefault() == "--live-social")
{
    await SocialImportTests.LiveAsync(args.Skip(1).ToArray());
    return;
}

var count = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); count++; }
using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "quantity-cases.json")));
foreach (var test in cases.RootElement.GetProperty("quantities").EnumerateArray())
{
    var input = test.GetProperty("input").GetString()!;
    Check(QuantityText.Parse(input)?.Canonical == test.GetProperty("expected").GetString(), "Quantity: " + input);
}
foreach (var test in cases.RootElement.GetProperty("ingredients").EnumerateArray())
{
    var input = test.GetProperty("input").GetString()!;
    var actual = IngredientTextParser.Parse(input);
    Check(actual.Name == test.GetProperty("name").GetString(), "Ingredient name: " + input + " -> " + actual.Name);
    Check(actual.Quantity == test.GetProperty("quantity").GetString(), "Ingredient quantity: " + input + " -> " + actual.Quantity);
    Check(actual.Unit == test.GetProperty("unit").GetString(), "Ingredient unit: " + input);
    Check(actual.Note == test.GetProperty("note").GetString(), "Ingredient note: " + input);
    Check(actual.RequiresReview == test.GetProperty("requiresReview").GetBoolean(), "Ingredient review: " + input);
    Check(actual.OriginalText == input, "Original text: " + input);
}
// UCUM 2.2 reference definitions: grain, inch, US/imperial gallon.
// Verify exact factors independently of the rounded ingredient-output examples.
var pound = Rational.FromDecimal("64.79891").Multiply(new(7000, 1000));
var inch = Rational.FromDecimal("2.54");
var usGallon = inch.Multiply(inch).Multiply(inch).Multiply(new(231, 1));
var imperialGallon = Rational.FromDecimal("4.54609").Multiply(new(1000, 1));
foreach (var reference in new (string Alias, Rational Factor)[] {
    ("lb",pound), ("oz",pound.Multiply(new(1,16))),
    ("us cup",usGallon.Multiply(new(1,16))), ("us fluid ounce",usGallon.Multiply(new(1,128))),
    ("imperial cup",imperialGallon.Multiply(new(1,16))), ("imperial fluid ounce",imperialGallon.Multiply(new(1,160))),
    ("ucum metric cup",new(240,1)), ("metric fluid ounce",new(30,1)) })
{
    var factor = UnitNormalization.Match(reference.Alias + " ingredient")!.Value.Definition!.Factor;
    Check(Rational.FromDecimal(factor).Compare(reference.Factor) == 0, "Exact authoritative unit factor: " + reference.Alias);
}

// Exercise the real JSON-LD extractor, including PropertyValue, not just the line parser.
var html = """
<html><script type="application/ld+json">{"@context":"https://schema.org","@type":"Recipe",
"name":"Test recipe","recipeYield":"2 servings","recipeIngredient":["1-2 lb beef","3-4 Eier","Fett für das Blech",
{"@type":"PropertyValue","name":"milk","value":"1/2-1","unitText":"US cup"}],
"recipeInstructions":[{"@type":"HowToStep","text":"Cook."}],"image":"https://example.com/photo.png"}</script></html>
""";
var extracted = new JsonLdRecipeExtractor().Extract(html, new Uri("https://example.com/recipe"));
Check(extracted.Draft.Ingredients[0].Quantity == "453.592-907.185", "Website range conversion");
Check(extracted.Draft.Ingredients[1].Unit == "piece", "Website implicit piece");
Check(extracted.Draft.Ingredients[2].Quantity == "" && extracted.Draft.Ingredients[2].RequiresReview, "Incomplete import review");
Check(extracted.Draft.Ingredients[3].Quantity == "118.294-236.588", "PropertyValue fractions/range");
Check(extracted.Draft.ExternalImageUrl == "https://example.com/photo.png", "Website photo preserved");
Check(extracted.Draft.Servings == 2 && extracted.Draft.Steps.Single() == "Cook.", "Website fields preserved");
var validInput = new RecipeInput("Test", null, null, 2, [new("eggs", "3 bis 4", "piece", null), new("salt", "", "toTaste", null)], ["Cook"]);
Check(RecipeValidation.Check(validInput).Count == 0, "String/range input accepted");
foreach (var invalid in new string?[] { null, "", "1/0", "-1", "4-3", "100001", "0.0001", new('1', 65) })
{
    var input = validInput with { Ingredients = [new("eggs", invalid, "piece", null)] };
    Check(RecipeValidation.Check(input).ContainsKey("ingredients[0].quantity"), "Normal save rejected: " + invalid);
}
Check(RecipeValidation.Check(validInput with { Ingredients = [new("salt", null, "toTaste", null)] }).Count > 0, "toTaste requires empty string");
Check(RecipeValidation.Check(validInput with { Ingredients = [new("beef", "1", "lb", null)] }).Count > 0, "No persisted non-metric unit");
var recipe = new Recipe(); RecipeMapping.Apply(recipe, validInput);
Check(recipe.Ingredients[0].Quantity == "3-4", "Range normalized at save");

// Migrate a real SQLite database at the old migration with representative invariant decimals.
var directory = Path.Combine(Path.GetTempPath(), "recipeatlas-migration-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
try
{
    var options = new DbContextOptionsBuilder<RecipeDb>().UseSqlite("Data Source=" + Path.Combine(directory, "recipes.db")).Options;
    await using var db = new RecipeDb(options);
    var migrator = db.GetService<IMigrator>();
    await migrator.MigrateAsync("20261008161847_InitialCreate");
    var oldCulture = CultureInfo.CurrentCulture;
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
    try
    {
        var existing = new Recipe { Title = "Existing", Servings = 2, Steps = [new() { Position = 0, Instruction = "Cook" }], Image = new() { ContentType = "image/png", Bytes = [1, 2, 3, 4] } };
        db.Add(existing); await db.SaveChangesAsync();
        var oldQuantities = new decimal[] { 2m, 0.5m, 0.001m, 100000m, 1.230m, 12345.678m };
        for (var i = 0; i < oldQuantities.Length; i++)
            await db.Database.ExecuteSqlRawAsync("INSERT INTO Ingredient (RecipeId, Position, Name, Quantity, Unit, Note) VALUES ({0},{1},{2},{3},'g','existing note')", existing.Id, i, "ingredient " + i, oldQuantities[i]);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Ingredient (RecipeId, Position, Name, Quantity, Unit) VALUES ({0},6,'salt',NULL,'toTaste')", existing.Id);
        var before = await ReadRows(db);
        for (var i = 0; i < oldQuantities.Length; i++)
            Check(decimal.TryParse(before[i][4], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var invariant) && invariant == oldQuantities[i], "Existing numeric data uses invariant text: " + i);
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        var after = await ReadRows(db);
        Check(before.Count == after.Count, "Migration count");
        for (var i = 0; i < before.Count; i++)
        {
            var expected = before[i].ToArray();
            expected[4] ??= "";
            Check(expected.SequenceEqual(after[i]), "Migration retained row/decimal/order/relationship: " + i);
        }
        var reloaded = await db.Recipes.Include(r => r.Ingredients).Include(r => r.Steps).Include(r => r.Image).SingleAsync();
        Check(reloaded.Title == "Existing" && reloaded.Servings == 2, "Recipe preserved");
        Check(reloaded.Steps.Single().Instruction == "Cook", "Steps preserved");
        Check(reloaded.Image!.Bytes.SequenceEqual(new byte[] { 1, 2, 3, 4 }), "Image preserved");
        Check(reloaded.Ingredients.Single(i => i.Unit == "toTaste").Quantity == "", "toTaste migrated");
        RecipeMapping.Apply(reloaded, validInput); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        reloaded = await db.Recipes.Include(r => r.Ingredients).SingleAsync();
        Check(reloaded.Ingredients.OrderBy(i => i.Position).First().Quantity == "3-4", "Save/reload range");
        await migrator.MigrateAsync(); Check((await ReadRows(db)).Count == 2, "Migration repeatable");
        await db.Recipes.ExecuteDeleteAsync();
        Check(!await db.Set<Ingredient>().AnyAsync() && !await db.Images.AnyAsync() && !await db.Set<RecipeStep>().AnyAsync(), "Foreign-key cascades retained");
    }
    finally { CultureInfo.CurrentCulture = oldCulture; }
}
finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
await SocialImportTests.RunAsync(Check);
Console.WriteLine($"Passed {count} quantity, website/social/text import, process, validation and SQLite migration checks.");

static async Task<List<string?[]>> ReadRows(RecipeDb db)
{
    await db.Database.OpenConnectionAsync();
    await using var cmd = db.Database.GetDbConnection().CreateCommand();
    cmd.CommandText = "SELECT Id,RecipeId,Position,Name,Quantity,Unit,Note FROM Ingredient ORDER BY Position";
    await using var reader = await cmd.ExecuteReaderAsync(); var rows = new List<string?[]>();
    while (await reader.ReadAsync()) rows.Add(Enumerable.Range(0, 7).Select(i => reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)).ToArray());
    return rows;
}
