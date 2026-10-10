# Ingredient quantity strings

Develop these changes on backend `WebsiteScraping` and frontend `WebsiteReipeEctraction` (the existing frontend branch spelling). Deploy both together: the API now accepts and returns strings for `quantity`, not JSON numbers or null. An older frontend cannot send the new contract correctly. A range cannot be represented by the old backend.

## Storage and validation

Examples: `"2"`, `"0.5"`, `"1/2"`, `"1 1/2"`, `"3-4"`, `"1/2-1 1/2"`. Decimal commas become points. `-`, `–`, `—`, `to`, and `bis` become `-` in stored ranges. Supported Unicode fractions become ASCII fractions. Fractions are retained without first approximating them as floating point. Both endpoints must be between 0.001 and 100000 inclusive, in ascending order. Quantity input and normalized output are limited to 64 characters. Malformed, negative, zero-denominator, zero and out-of-bounds quantities are rejected. `toTaste` uses exactly `""`.

The website importer and quick entry share unit definitions and matching quantity/ingredient test cases with C#. Incomplete website drafts use `""` and `requiresReview`, with original source text and a reason. Saving still requires a valid quantity and supported unit, except for `toTaste`. Review flags must be confirmed in the editor. Packaging counts use existing `pack`/`can` codes and the new `jar` code; `à 175 g` and preparation descriptions remain notes. No jar or glass volume is invented. Missing units default to `piece` only after explicit recognized units have been considered. Unknown measurement systems/recognized unsupported units remain unresolved with their source text. A line without an amount remains unresolved unless it explicitly says `to taste` or `nach Geschmack`.

## Conversion policy

| Source | Final unit | Exact factor | Policy |
| --- | --- | --- | --- |
| lb, lbs, pound(s) | g | 453.59237 | Avoirdupois mass |
| oz, ounce(s) | g | 28.349523125 | Weight; explicit fluid variants are handled first |
| US customary cup | ml | 236.5882365 | Default for unspecified cup, flagged for review |
| Imperial cooking cup | ml | 284.130625 | Defined as half an imperial pint |
| Metric cooking cup | ml | 250 | Cooking convention, flagged for review because definitions vary |
| UCUM metric cup / US legal cup | ml | 240 | Explicit definition |
| US fluid ounce | ml | 29.5735295625 | Default for unspecified fluid ounce, flagged for review |
| Imperial fluid ounce | ml | 28.4130625 | Explicit UK/imperial |
| Metric fluid ounce | ml | 30 | UCUM convention, flagged for review |
| German Pfund | g | 500 | Modern German recipe assumption, flagged; never treated as English lb |

Conversion multiplies both endpoints with exact rational arithmetic. Round only the final converted string to 3 decimal places, half away from zero, without trailing zeros or scientific notation. Bounds are checked before rounding; a tiny positive amount is not silently changed to zero. Original fractions are otherwise kept. Cups become volume, never a universal gram amount: density would be needed. `TL`/`EL` retain the application's metric 5/15 ml definitions. English bare spoon aliases retain the website importer's review warning; explicit US spoons are converted to ml.

The authoritative [UCUM 2.2 definitions](https://github.com/ucum-org/ucum/blob/ef4c31cd7d3bc81de1a1bf2cc8414bf502b6304f/ucum-essence.xml) were retrieved and checked: pound = 7000 grains, grain = 64.79891 mg, ounce = pound/16; US gallon = 231 cubic international inches, inch = 2.54 cm; US cup = 16 US tablespoons; imperial gallon = 4.54609 l, fluid ounce = imperial gallon/160. UCUM metric cup is 240 ml and metric fluid ounce 30 ml, hence the separate documented/reviewed 250 ml cooking convention. NIST SP 811 and UK Weights and Measures Act references are also recorded in `MeasurementUnits.json`.

## Migrate an existing development database

Stop the running API and back up the entire data directory, including cookie keys and SQLite journal/WAL files. Do not delete the database or initial migration. If `DataDirectory` was overridden, use that directory instead of the default path below.

From the cloud shell, activate its retained SDK/cache environment:

```sh
source /workspace/.recipeatlas-setup/env.sh
```

From `/workspace/RecipeAtlas`:

```sh
dotnet restore
dotnet tool restore
dotnet build
mkdir -p backend/RecipeAtlas.Api/data
dotnet ef database update --project backend/RecipeAtlas.Api --connection "Data Source=/workspace/RecipeAtlas/backend/RecipeAtlas.Api/data/recipes.db"
dotnet run --no-build --project backend/RecipeAtlas.Api --launch-profile http
```

Keep the existing `Owner:PasswordHash` user secret or `Owner__PasswordHash` environment binding. It is not changed by this migration. EF's design-time factory points at `design-time.db`, so the explicit connection above matters. API startup also applies pending migrations to its configured database automatically.

From `/workspace/RecipeAtlasFrontend`, in another activated shell:

```sh
npm ci
npm start
```

`StringIngredientQuantities` preserves the invariant TEXT decimal values already written by EF SQLite, without passing them through REAL/float. Null quantities become empty strings; null `toTaste` therefore becomes `""`. SQLite rebuilds only the Ingredient table to make the string column required, copying IDs, order, notes and recipe IDs and recreating its index/foreign key. Recipes, steps, images and cookie keys are preserved. See the reviewed `string-ingredient-quantities.sql`. No application database was deleted or reset during development.

Downgrade is deliberately refused: new fractions/ranges cannot be represented by the previous decimal contract. For rollback, restore the pre-upgrade data backup together with both old application versions. In production, stop writes, take the backup, replace backend and frontend together, then start one API instance (its normal startup applies the migration). Existing deployment networking, authentication and image handling are unchanged.

## Tests

From `/workspace/RecipeAtlas`:

```sh
dotnet build
dotnet run --project tests/RecipeAtlas.Tests
python3 tests/integration.py
python3 tests/check_shared_contracts.py ../RecipeAtlasFrontend
dotnet ef migrations has-pending-model-changes --project backend/RecipeAtlas.Api
```

The C# console regression runner has no additional test-package dependencies. It exercises quantity/ingredient cases, the actual JSON-LD extractor (including PropertyValue), normal validation and EF migration of an old SQLite database under German culture. The Python integration suite starts isolated API instances and checks string/range save/reload, validation, auth, images and persistence after restart. The cross-repository check ensures the unit definition and test-case copies agree; update them in both repositories together.

From `/workspace/RecipeAtlasFrontend`:

```sh
npm test
npm run build
```

Tests cover manual text entry, unit switching, quick conversions, draft review/correction, existing recipe editing, photo-upload retry and serving changes. Serving quantities are calculated from the stored original each time, scale both endpoints, and never change the saved recipe. An invalid stored quantity is displayed verbatim with “not scaled”.
