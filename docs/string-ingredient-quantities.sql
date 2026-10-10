BEGIN TRANSACTION;
UPDATE Ingredient SET Quantity = CAST(Quantity AS TEXT) WHERE Quantity IS NOT NULL;

UPDATE Ingredient SET Quantity = '' WHERE Quantity IS NULL;

CREATE TABLE "ef_temp_Ingredient" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Ingredient" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Note" TEXT NULL,
    "Position" INTEGER NOT NULL,
    "Quantity" TEXT NOT NULL,
    "RecipeId" TEXT NOT NULL,
    "Unit" TEXT NOT NULL,
    CONSTRAINT "FK_Ingredient_Recipes_RecipeId" FOREIGN KEY ("RecipeId") REFERENCES "Recipes" ("Id") ON DELETE CASCADE
);

INSERT INTO "ef_temp_Ingredient" ("Id", "Name", "Note", "Position", "Quantity", "RecipeId", "Unit")
SELECT "Id", "Name", "Note", "Position", IFNULL("Quantity", ''), "RecipeId", "Unit"
FROM "Ingredient";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "Ingredient";

ALTER TABLE "ef_temp_Ingredient" RENAME TO "Ingredient";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE INDEX "IX_Ingredient_RecipeId" ON "Ingredient" ("RecipeId");

COMMIT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261010112715_StringIngredientQuantities', '10.0.12');
