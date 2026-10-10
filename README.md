# RecipeAtlas backend

A private, single-owner recipe API. C# / ASP.NET Core 10, EF Core 10, SQLite.

This package contains the complete backend source, an initial database migration, an integration test, and Docker deployment files. It does not contain a frontend. Run one API instance with a persistent local disk.

## Design

| Concern | Choice |
|---|---|
| HTTP API | ASP.NET Core Minimal APIs |
| Data access | EF Core used directly in endpoints |
| Database | SQLite, one file on persistent disk |
| Pictures | One optional JPEG, PNG, or WebP per recipe, stored in SQLite |
| Login | One owner password, salted PBKDF2 hash, encrypted HttpOnly cookie |
| Browser writes | Required X-RecipeAtlas header, no cross-origin API access |
| Deployment | One API container behind Caddy HTTPS |

No separate database server is needed. Recipes, ingredients, steps, and images are saved together in the database. Storing a few hundred bounded photos is a reasonable starting point for a personal cookbook. If the collection becomes large, move images to object storage and add thumbnails.

The API returns small paginated cards for the recipe list. It downloads ingredients and steps only for a detail request, and image bytes only for an image request. Responsive layouts, touch controls, and image resizing belong in your frontend.

## 1. Put these files into your existing repository

Extract the ZIP. Copy the contents of its `RecipeAtlas` folder into your existing empty Git checkout. Include `.config`, `.gitignore`, `.dockerignore`, and `.env.example`.

Do not run `git init` again. The resulting repository root contains:

| Path | Purpose |
|---|---|
| RecipeAtlas.slnx | Solution |
| backend/RecipeAtlas.Api/Program.cs | Configuration, security, startup |
| backend/RecipeAtlas.Api/OwnerAuth.cs | Password hashing, login, logout |
| backend/RecipeAtlas.Api/Models.cs | Database entities and unit definitions |
| backend/RecipeAtlas.Api/RecipeDb.cs | EF Core configuration |
| backend/RecipeAtlas.Api/Contracts.cs | Request/response types and validation |
| backend/RecipeAtlas.Api/RecipeEndpoints.cs | CRUD, search, and images |
| backend/RecipeAtlas.Api/Migrations/ | Versioned schema |
| tests/integration.py | Isolated end-to-end API checks |
| sample-recipe.json | Working create/update payload |
| Dockerfile, compose.yaml, deploy/Caddyfile | HTTPS deployment |

Run the following commands from that repository root unless stated otherwise.

## 2. Install .NET 10 SDK

Install the SDK, not only the runtime: https://dotnet.microsoft.com/download/dotnet/10.0

On CachyOS / Arch Linux:

```sh
sudo pacman -Syu dotnet-sdk-10.0 aspnet-runtime-10.0
dotnet --version
dotnet --list-runtimes
```

The SDK should show `10.0.x`. The runtime list should include `Microsoft.AspNetCore.App 10.0.x`.

The application packages and local EF tool are pinned to 10.0.12. Future SDK patches within .NET 10 can build this project. Commands below work in Bash and Fish. On Windows, use the official SDK installer and PowerShell. Use `curl.exe` where PowerShell aliases `curl`.

## 3. Restore and build

```sh
dotnet restore
dotnet tool restore
dotnet build
```

The EF tool is local to this repository. You do not need a global dotnet-ef installation. SQLite does not need a service, container, or separate installation.

## 4. Set your owner password

Generate a hash. Enter a password of at least 12 characters when prompted. Nothing is echoed while you type.

```sh
dotnet run --project backend/RecipeAtlas.Api -- --hash-password
```

Copy only the generated hash into this command:

```sh
dotnet user-secrets set "Owner:PasswordHash" "PASTE_HASH_HERE" --project backend/RecipeAtlas.Api
```

The hash is stored outside the repository in your local .NET user secrets. User secrets are a development convenience, not encrypted storage. Production uses a separate environment variable. There is no username, registration endpoint, or password reset email.

To change the password, generate a new hash, replace the configured hash, and restart the API. Existing cookies then stop working. A successful login lasts 14 days without extending itself automatically.

## 5. Start the API

```sh
dotnet run --project backend/RecipeAtlas.Api --launch-profile http
```

Open http://localhost:5080/health. Expect `{"status":"ok"}`.

The first start applies the included migration and creates:

```text
backend/RecipeAtlas.Api/data/recipes.db
backend/RecipeAtlas.Api/data/keys/
```

The database contains all recipe content and photos. The `keys` folder keeps authentication cookies valid across restarts. Both are ignored by Git. The health endpoint reports that startup completed. Recipe endpoints require login and return 401 otherwise.

## 6. Try a recipe

In the repository root, create `login.json` with your actual password:

```json
{"password":"YOUR_ACTUAL_PASSWORD"}
```

This filename is ignored by Git. Use it only for local testing, then delete it along with `cookies.txt`.

Log in and save the cookie:

```sh
curl -i -c cookies.txt -H 'Content-Type: application/json' -H 'X-RecipeAtlas: 1' --data-binary @login.json http://localhost:5080/api/auth/login
```

Expect 204. Add the supplied sample recipe:

```sh
curl -i -b cookies.txt -H 'Content-Type: application/json' -H 'X-RecipeAtlas: 1' --data-binary @sample-recipe.json http://localhost:5080/api/recipes
```

Expect 201 and a recipe object. Copy its `id` and replace `RECIPE_ID` below.

```sh
curl -b cookies.txt 'http://localhost:5080/api/recipes?search=pasta&page=1&pageSize=20'
curl -b cookies.txt http://localhost:5080/api/recipes/RECIPE_ID
curl -b cookies.txt http://localhost:5080/api/units
```

Edit `sample-recipe.json`, then replace the saved recipe's text and ingredients:

```sh
curl -X PUT -b cookies.txt -H 'Content-Type: application/json' -H 'X-RecipeAtlas: 1' --data-binary @sample-recipe.json http://localhost:5080/api/recipes/RECIPE_ID
```

Upload a picture from your computer. The body is raw file bytes, not multipart/form-data:

```sh
curl -X PUT -b cookies.txt -H 'Content-Type: image/jpeg' -H 'X-RecipeAtlas: 1' --data-binary @pasta.jpg http://localhost:5080/api/recipes/RECIPE_ID/image
```

Remove only the image:

```sh
curl -X DELETE -b cookies.txt -H 'X-RecipeAtlas: 1' http://localhost:5080/api/recipes/RECIPE_ID/image
```

Delete the recipe, including its ingredients, steps, and image:

```sh
curl -X DELETE -b cookies.txt -H 'X-RecipeAtlas: 1' http://localhost:5080/api/recipes/RECIPE_ID
```

## 7. Understand the API contract

All endpoints except login and health require the session cookie. POST, PUT, and DELETE requests require `X-RecipeAtlas: 1`. JSON bodies use `Content-Type: application/json`.

| Method | Path | Result |
|---|---|---|
| GET | /health | Startup/liveness response |
| POST | /api/auth/login | 204 and cookie, body `{ "password": "..." }` |
| GET | /api/auth/me | Current owner, or 401 |
| POST | /api/auth/logout | 204 and cookie removal |
| GET | /api/units | Available unit codes and labels |
| GET | /api/recipes | Paginated recipe cards |
| GET | /api/recipes/{id} | Full recipe |
| POST | /api/recipes | Create, 201 with full recipe and Location header |
| PUT | /api/recipes/{id} | Replace text, servings, ingredients, and steps |
| DELETE | /api/recipes/{id} | Delete recipe, 204 |
| GET | /api/recipes/{id}/image | Authenticated image bytes |
| PUT | /api/recipes/{id}/image | Upload or replace image, 200 with imageUrl |
| DELETE | /api/recipes/{id}/image | Remove image, 204 |

List query parameters are `search`, `page` (default 1), and `pageSize` (default 20, maximum 100). Search matches substrings of titles or ingredient names. SQLite's default LIKE handles ASCII case-insensitivity. Full Unicode case folding and fuzzy search are not included.

A list response has this structure:

```json
{
  "items": [],
  "total": 0,
  "page": 1,
  "pageSize": 20
}
```

For POST and PUT, use the complete structure in `sample-recipe.json`. The order of the ingredients and steps arrays is preserved. PUT is full replacement of those fields, not a partial patch. It leaves the image alone. Fields returned by the API such as `id`, `imageUrl`, and timestamps are not editable input fields.

Title, servings, at least one ingredient, and at least one step are required. Description, source URL, ingredient notes, and photo are optional. The API stores the source URL as a reference. It does not download or import linked recipes.

| Unit code | Meaning | Quantity |
|---|---|---|
| g | Grams | Positive quantity string |
| kg | Kilograms | Positive quantity string |
| ml | Milliliters | Positive quantity string |
| l | Liters | Positive quantity string |
| tsp | Metric teaspoon, 5 ml | Positive quantity string |
| tbsp | Metric tablespoon, 15 ml | Positive quantity string |
| piece | Piece, such as an egg or garlic clove | Positive quantity string |
| pinch | Pinch, approximate | Positive quantity string |
| toTaste | To taste | Empty string |

Quantities are strings, such as `"0.5"`, `"1/2"`, `"1 1/2"`, or `"3-4"`. Decimal commas and range separators are normalized on save. `toTaste` uses `""`. The API does not convert mass to volume or infer ingredient density. For canned ingredients use grams or milliliters and put the packaging detail in `note`. Spoon measurements differ by region, so this API explicitly uses metric spoons. Website extraction converts supported cup/fluid-ounce definitions to ml and marks unspecified definitions for review.

Validation returns 400 with an `errors` dictionary. Other relevant statuses are 401 (login required), 403 (missing write header), 404 (missing recipe/image), 413 (image too large), 415 (unsupported image signature), and 429 (login limit). Ten login attempts are allowed per 15-minute fixed window for the entire single-owner app, including successful attempts. This counter resets on process restart.

## 8. Connect your mobile-friendly frontend

Serve the frontend and API from the same origin. Use relative URLs such as `/api/recipes`. During development, configure your frontend development server to proxy `/api` requests to `http://localhost:5080`. Open the frontend through its own local URL. The browser then receives the login cookie on that origin.

For an Angular frontend, put this in `proxy.conf.json` in the frontend project:

```json
{
  "/api/**": {
    "target": "http://localhost:5080",
    "secure": false
  }
}
```

Start Angular with `ng serve --proxy-config proxy.conf.json`. If your framework uses another development server, configure the equivalent proxy there. Do not solve this by enabling wildcard CORS.

These plain browser fetch examples work through the proxy or on your production origin:

```js
async function api(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    credentials: "same-origin",
    headers: {
      "X-RecipeAtlas": "1",
      ...(options.headers ?? {})
    }
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw Object.assign(new Error(problem.title ?? `HTTP ${response.status}`), {
      status: response.status,
      errors: problem.errors ?? {}
    });
  }
  return response.status === 204 ? null : response.json();
}

await api("/api/auth/login", {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ password })
});

const saved = await api("/api/recipes", {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify(recipeFormValue)
});

// selectedFile is a File from <input type="file">.
if (selectedFile) {
  await api(`/api/recipes/${saved.id}/image`, {
    method: "PUT",
    headers: { "Content-Type": selectedFile.type || "application/octet-stream" },
    body: selectedFile
  });
}
```

Fetch the recipe again after upload, then use its `imageUrl` in `<img loading="lazy">`. Same-origin image requests carry the HttpOnly cookie automatically. Keep cookie credentials out of localStorage.

Use `accept="image/jpeg,image/png,image/webp"` on the file input. HEIC is not accepted. Resize phone pictures to approximately 1600 pixels on the longest side and encode them as JPEG/WebP before upload. This API enforces a 5 MiB limit but does not compress, resize, remove EXIF metadata, or fully decode images. It screens binary signatures and serves the detected MIME type with `nosniff`.

Create the recipe first and upload its optional image second. If upload fails, keep the saved recipe ID and retry the image request. Do not create another recipe on retry. Disable Save while a request is running to avoid double submission. Render titles, descriptions, and steps as text, not injected HTML.

## 9. Run the integration checks

Requires Python 3, with no additional Python packages:

```sh
dotnet build
python3 tests/integration.py
```

The test starts its own server with a random password, temporary database, and separate port. It checks authentication, write protection, validation, search, replacement of ingredients/steps, photo upload/deletion, size limits, recipe deletion, and persistence across restart. It never touches your recipe database.

Verification performed for this package: .NET SDK 10.0.401 / runtime 10.0.12, build succeeded with zero warnings and zero errors, and all 34 HTTP integration checks passed. Docker and public TLS deployment were not executed in the preparation environment.

## 10. Deploy for access from anywhere

Use a Linux server that runs Docker Engine and Docker Compose. This setup needs persistent local storage and one API replica. It is not configured for stateless serverless hosting or horizontal scaling.

1. Point a domain such as `recipes.example.com` at your server's public IP. If you publish an AAAA record, IPv6 must also reach the server.
2. Allow inbound TCP ports 80 and 443 in the server/provider firewall.
3. Clone your repository onto the server and change into its root.
4. Copy `.env.example` to `.env` and restrict its permissions:

```sh
cp .env.example .env
chmod 600 .env
```

5. Edit `.env`. Set your real domain and the password hash generated in step 4. The domain must be a hostname without `https://` or a path. The hash, not the plaintext password, goes into `RECIPE_ATLAS_PASSWORD_HASH`.
6. Build and start:

```sh
docker compose up -d --build
docker compose logs --tail=100 api caddy
```

Open `https://YOUR_DOMAIN/health`. Caddy obtains and renews the TLS certificate and redirects HTTP to HTTPS when DNS and inbound ports are correct. The API has no host port of its own. HTTPS terminates at Caddy, and production authentication cookies always use Secure.

The included Caddyfile initially proxies every request to the backend. `/` returns 404 until you add your frontend. To publish a built single-page frontend later, add this Caddy service volume in `compose.yaml`:

```yaml
- ./frontend-dist:/srv:ro
```

Put the built frontend files, including `index.html`, in `frontend-dist`. Replace `deploy/Caddyfile` with:

```caddyfile
{$RECIPE_ATLAS_DOMAIN} {
    header Strict-Transport-Security "max-age=31536000"
    @backend path /api /api/* /health
    handle @backend {
        reverse_proxy api:8080
    }
    handle {
        root * /srv
        try_files {path} /index.html
        file_server
    }
}
```

Recreate the services with `docker compose up -d`. For later Caddyfile-only edits, run `docker compose exec caddy caddy reload --config /etc/caddy/Caddyfile`.

## 11. Back up your data

The `recipe_data` Docker volume persists recipes, photos, and cookie keys. Rebuilding or replacing the API container preserves it. `docker compose down -v` deletes it.

For a consistent backup of this small personal app, briefly stop API writes, copy the entire data directory, then restart. These commands run from the repository root on the server:

```sh
mkdir -p backups
docker compose stop api
docker compose cp api:/app/data backups/recipe-data
docker compose start api
chmod -R go-rwx backups
```

Use a fresh destination directory for each backup. Copy backups off the server. Protect backups because they include your recipes and authentication keys. Do not copy only `recipes.db` while the server is running, since SQLite may also have journal/WAL files.

For a restore, stop the API, preserve the current data as another backup, then copy the backed-up directory contents into `/app/data` in the stopped API container with `docker compose cp`. Ensure the data remains writable by the container's `app` user before restarting. Example after preserving the current data:

```sh
docker compose stop api
docker compose run --rm --no-deps --user root --entrypoint sh api -c 'rm -rf /app/data/*'
docker compose cp backups/recipe-data/. api:/app/data/
docker compose run --rm --no-deps --user root --entrypoint sh api -c 'chown -R app:app /app/data'
docker compose start api
```

## 12. Commit and evolve the backend

```sh
git add .
git status
git commit -m "Add RecipeAtlas backend"
git push
```

Confirm that `.env`, `login.json`, `cookies.txt`, and the `data` directory are absent from the staged files.

For later model changes, generate and commit a new migration:

```sh
dotnet ef migrations add DescribeYourChange --project backend/RecipeAtlas.Api
dotnet build
python3 tests/integration.py
```

Inspect the migration and back up production before deploying. The API applies pending migrations at startup. This is a deliberate choice for a single-instance personal app. Never replace migrations with `EnsureCreated`, and do not delete existing migration history once you have real data.

This first version uses last-write-wins for edits. Editing the same recipe simultaneously on two devices can overwrite an earlier edit. It does not include offline synchronization, recipe scraping, public sharing, multiple accounts, or duplicate-request deduplication. Add those only when you need them.

## Official references

- .NET support policy: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- Cookie authentication: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0
- EF Core SQLite provider: https://learn.microsoft.com/en-us/ef/core/providers/sqlite/
- EF Core migrations: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying
- Arch .NET SDK package: https://archlinux.org/packages/extra/x86_64/dotnet-sdk-10.0/
- Caddy HTTPS: https://caddyserver.com/docs/quick-starts/https

## String quantities upgrade

See [quantity contracts, conversion assumptions, migration and coordinated deployment](docs/quantity-strings.md). The initial migration remains intact; `StringIngredientQuantities` upgrades existing values without resetting recipe data.
