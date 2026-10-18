# Recipoke backend

Invitation-only recipe application: ASP.NET Core 10, Identity cookies, EF Core 10 and one SQLite database. Recipes and images belong to cookbooks with Owner, Editor and Viewer access. Global administrators manage accounts and invitations; that capability grants no automatic access to private recipes.

The backend remains a feature-organised monolith with an independent domain assembly. Application operations use EF Core directly; endpoints handle HTTP. See the German [architecture guide](docs/architecture/README.md), [account/cookbook decisions](docs/architecture/adr/009-identity-sessions.md) and [upgrade/runbook](docs/accounts-and-cookbooks.md).

Both repositories must use `feature/accounts-and-cookbooks` together. This feature changes authentication and recipe routes and adds a migration; it is not a rename-only upgrade.

## Local setup

Requirements: .NET SDK **10.0.401** (see `global.json`), Python 3 for checks. No database server is needed. Run from the backend checkout:

```sh
dotnet restore --locked-mode
dotnet tool restore
dotnet build --no-restore
dotnet user-secrets set 'Accounts:PublicAppUrl' 'http://localhost:4200' --project backend/RecipeAtlas.Api
# Applies pending migrations, prompts for username/display name/password, then exits.
dotnet run --no-build --project backend/RecipeAtlas.Api -- --bootstrap-admin
dotnet run --project backend/RecipeAtlas.Api --launch-profile http
```

The password prompt does not echo. Choose 12–256 characters; there is no default password or public registration route. The initial administrator can add an email on the Account page. Do not put passwords into arguments, shell history or tracked files. The CLI accepts redirected standard input for a protected secret-input channel; do not save it in the repository. A completed bootstrap is a no-op and never resets credentials. Deliberate recovery uses `--recover-admin` instead.

Open `http://localhost:5080/health`. This is liveness, not account-setup readiness: all `/api` requests return 503 until bootstrap completes. In the matching frontend checkout:

```sh
npm ci
npm run check:contracts
npm test
npm run build
npm start
```

Visit `http://localhost:4200`. Angular proxies `/api` to 5080. Choose a cookbook before creating/importing recipes. Accounts administration issues a one-use invitation link; share it privately. See the [two-user demonstration](docs/accounts-and-cookbooks.md#two-user-demonstration).

`/workspace/.setup/env.sh` is only a cloud activation script, not a local prerequisite. Run the commands with your installed SDK/Python/Node; do not source that file on your computer.

## Persistence, compatibility and upgrade

Default content root remains `backend/RecipeAtlas.Api/`; data remains `data/recipes.db` and `data/keys/` beneath it unless `DataDirectory` is configured. Images remain SQLite blobs, not files. Keep the directory, Compose project name and volume mounts unchanged.

**Before upgrading an existing deployment, stop writes and back up the entire data directory and configuration. Follow [the upgrade procedure](docs/accounts-and-cookbooks.md#upgrade-an-existing-installation).** Startup still applies pending migrations. `AccountsAndCookbooks` preserves all old rows and IDs, assigns recipes to a reserved personal cookbook, and keeps access closed until the server-side administrator command finishes atomically. Historical migrations are unchanged. Downgrade requires restoring the pre-upgrade backup with both old binaries.

| Identifier | Decision |
|---|---|
| `Recipoke.slnx`, `Recipoke.Api`, `Recipoke.Domain`, `tests/Recipoke.Tests` | Current solution/projects |
| `backend/RecipeAtlas.Api`, GitHub repositories/remotes | Retained to preserve content root and checkout conventions |
| `X-RecipeAtlas: 1` | Retained same-origin browser write protection; no CORS |
| Data-protection application `RecipeAtlas`, `data/keys`, `RecipeAtlas.LocalDevelopment` user-secrets ID | Retained configuration/key locations |
| `DataDirectory`, `recipes.db`, `recipe_data`, `/app/data` | Retained storage locations; never recreate user data |
| `RecipeText`, `SocialImport`, `RECIPE_ATLAS_DOMAIN` | Retained import/domain configuration |
| Old `Owner:PasswordHash`, `Owner__PasswordHash`, `RECIPE_ATLAS_PASSWORD_HASH` | Obsolete and ignored; no conversion to Identity passwords |
| Old `RecipeAtlas.Session` cookie | Deliberately rejected; fresh username/password login required |
| New `Recipoke.Session` cookie | Identity scheme, HttpOnly, SameSite Strict, Secure in Production, `/api`, 14 days without sliding |

`RECIPOKE_DOTNET` takes precedence over legacy `RECIPE_ATLAS_DOTNET` in the existing integration runner; neither changes server configuration. No credentials or session tokens are stored in browser storage.

## API contract

Generated snapshot: [contracts/recipoke.openapi.json](contracts/recipoke.openapi.json). `/openapi/v1.json` is exposed only in Development. All API responses use `Cache-Control: no-store`; protected images never return an unauthorised 304. All state-changing requests, including public invitation requests, require `X-RecipeAtlas: 1`. JSON commands use `application/json` and the existing ProblemDetails/validation error format.

| Area | Routes |
|---|---|
| Session | `POST /api/auth/login` with username/password; `GET /api/auth/me`; `POST /api/auth/logout`; `POST /api/auth/password` with currentPassword/newPassword |
| Administrator | `GET /api/admin/accounts`; `PUT /api/admin/accounts/{id}/status` with isActive |
| Invitations | Admin `GET/POST /api/admin/invitations`, `DELETE /api/admin/invitations/{id}`; public `POST /api/invitations/validate` and `/redeem` |
| Cookbooks | `GET/POST /api/cookbooks`; `GET/PUT /api/cookbooks/{cookbookId}` |
| Membership | `GET/POST /api/cookbooks/{cookbookId}/members`; `PUT/DELETE .../members/{userId}`; `POST .../leave` |
| Recipes | `GET/POST /api/cookbooks/{cookbookId}/recipes`; `GET/PUT/DELETE .../recipes/{id}` |
| Images | `GET/PUT/DELETE /api/cookbooks/{cookbookId}/recipes/{id}/image` |
| Draft imports | `POST /api/recipes/import`, active user required; does not save |
| Units/liveness | Protected `GET /api/units`; public `GET /health` |

Old unscoped recipe routes are retired. Recipe JSON field names, quantity strings, ingredient/step ordering, pagination/search, image signature/5 MiB limit, review flags and import contracts are preserved. A cookbook is supplied through the route; ordinary updates cannot move recipes. The `imageUrl` now contains the cookbook route. Recipe text and image upload remain separate requests; the editor retries photos using the already saved ID.

Unauthenticated requests return 401, inaccessible cookbooks/resources 404, and insufficient permissions within accessible cookbooks 403. Login errors are generic. Five failed logins lock the account for 15 minutes; the existing shared login limiter allows ten attempts per 15-minute window. Invitation validation/redemption share 30 requests per minute. These in-process limits reset on restart. Password changes revoke other sessions; deactivation revokes all old sessions permanently. Reactivation requires a new login.

Invitation links use configured `Accounts:PublicAppUrl` (not the request Host). Default validity is 72 hours (`Accounts:InvitationHours`, 1–720). Links carry their random secret in a fragment, which the acceptance page removes from the address bar. The token is submitted in POST bodies, hashed in storage, and cannot be retrieved later. Never enable request/response-body logging or analytics on invitation flows.

## Verification and contract synchronisation

```sh
dotnet restore --locked-mode
dotnet tool restore
dotnet build --no-restore
dotnet run --no-build --project tests/Recipoke.Tests
python3 tests/integration.py
python3 tests/accounts_integration.py
python3 tests/ui_support_integration.py
python3 tests/test_contract_paths.py
dotnet ef migrations list --no-connect --no-build --project backend/RecipeAtlas.Api
dotnet ef migrations has-pending-model-changes --no-build --project backend/RecipeAtlas.Api
python3 scripts/export_openapi.py --check
python3 tests/check_shared_contracts.py ../RecipeAtlasFrontend
python3 scripts/sync_frontend_contracts.py ../RecipeAtlasFrontend --check
```

`dotnet test` does not run the console suite. Tests use disposable SQLite and synthetic credentials; no migration runs against user data. With `--no-connect`, EF cannot report applied/pending database state; this is expected. The model check separately compares code and snapshot.

Use the actual frontend root containing `package.json` in cross-repository commands. `../RecipeAtlasFrontend` only works for sibling checkouts. For intentional contract changes run `python3 scripts/export_openapi.py` then `python3 scripts/sync_frontend_contracts.py /absolute/frontend/root`, inspect both diffs and rerun checks. Domain unit definitions/backend fixtures remain authoritative.

Optional real-binary upgrade test (requires the complete published previous single-owner version):

```sh
python3 tests/upgrade_compatibility.py /path/to/old-publish/Recipoke.Api.dll
```

GitHub Actions runs the actual console runner, HTTP suites, model and OpenAPI checks. Frontend CI verifies its own copied contracts without requiring access to the private backend. Live scraping/AI and visual browser/mobile verification are separate checks, not implied by unit tests.

## Deployment

One API instance and persistent local disk, with same-origin HTTPS through Caddy. Keep the existing Compose project name/volumes. `.env.example` now needs the domain; Identity credentials reside in SQLite, not an environment password hash.

```sh
cp .env.example .env
chmod 600 .env
# Edit RECIPE_ATLAS_DOMAIN; retain existing provider secrets securely.
docker compose build
# On an upgrade: stop API writes and back up first; see the runbook.
docker compose run --rm --no-deps api --bootstrap-admin
docker compose up -d
docker compose logs --tail=100 api caddy
```

Bootstrap uses a terminal prompt. Recover an administrator with `docker compose run --rm --no-deps api --recover-admin` after stopping the API. A completed normal bootstrap does not change credentials. Do not use `down -v` or replace the data volume. The included backend Caddyfile proxies the API; use the matching frontend's deployment arrangement to serve the SPA and API on the same origin. Container/public TLS operation must be verified in your deployment.

## Imports and quantity rules

See [quantity strings/conversions](docs/quantity-strings.md) and [description-only social/text imports](docs/social-description-import.md). Website imports work without AI configuration. Social/text extraction uses the existing optional Gemini configuration and pinned yt-dlp setup. No new import capability is added by account sharing. External process networking still needs deployment-level egress isolation; HttpClient destination checks do not sandbox yt-dlp.

Deferred: account deletion/promotion UI, ownership transfer, password-reset delivery, shopping lists, cooking sessions, offline sync and general edit concurrency. Existing recipe edits remain last-write-wins.

## Mobile UI, profiles and membership consent

The latest feature update adds cookbook descriptions/covers, account display-name/email/avatar editing, and a private cookbook-invitation inbox. New members must accept before gaining access; at most five pending invitations are displayed and the next appears after a response. Existing accepted memberships remain unchanged. Owners can permanently delete a cookbook and all its recipes/images through a separate confirmation dialog.

Start the matching backend/frontend branches together after backing up the data directory. Normal startup applies `20261018100818_CookbookInvitationsAndProfiles`. **An already completed administrator setup does not need to run again, and existing Identity sessions remain usable.** The earlier single-owner upgrade instructions apply only if you are upgrading from that older version.

New API additions: `PUT /api/account/profile`, `PUT/DELETE /api/account/avatar`, protected `GET /api/accounts/{id}/avatar`, `GET /api/cookbook-invitations`, `POST /api/cookbook-invitations/{id}`, `PUT/GET/DELETE /api/cookbooks/{id}/image`, `DELETE /api/cookbooks/{id}`, and owner cancellation at `DELETE /api/cookbooks/{id}/invitations/{userId}`. Cookbook creation accepts description and initial `members` (username/role); `POST .../members` now invites rather than adding immediately. Profile email is not a new login identifier and no email delivery/verification is enabled.

See [ADR 013](docs/architecture/adr/013-cookbook-consent-and-profiles.md). Optional current-session upgrade check against a published pre-UI accounts version:

```sh
python3 tests/ui_upgrade.py /path/to/pre-ui-publish/Recipoke.Api.dll
```
