# Accounts and cookbooks: upgrade and operations

This guide applies to `feature/accounts-and-cookbooks` in **both** repositories. It supersedes old owner-hash setup instructions. API directory, SQLite filename, data-protection application/key location, import configuration, write header and volume names stay stable. Authentication and recipe URLs intentionally change together.

## Fresh local installation

From the backend root, with .NET SDK 10.0.401 installed:

```sh
dotnet restore --locked-mode
dotnet tool restore
dotnet build --no-restore
dotnet user-secrets set 'Accounts:PublicAppUrl' 'http://localhost:4200' --project backend/RecipeAtlas.Api
dotnet run --no-build --project backend/RecipeAtlas.Api -- --bootstrap-admin
dotnet run --project backend/RecipeAtlas.Api --launch-profile http
```

Bootstrap applies migrations and prompts for username, display name and a new password (12–256 characters, no composition rules). Password input is hidden in a terminal. Redirected stdin is supported for a secure secret-input channel; it must contain three lines. Do not put passwords in CLI arguments, a checked-in file or shell history. There is no public setup route. Old PBKDF2 owner hashes are not Identity passwords and are never reused.

In the frontend root:

```sh
npm ci
npm run check:contracts
npm test
npm run build
npm start
```

Sign in at `http://localhost:4200`; the proxy keeps cookies same-origin. Public application URL must be configured explicitly for invitation creation. HTTPS is required except for loopback development. Use the application's root URL without query/fragment or user information; do not take it from request headers. Invitation hours default to 72, configurable within 1–720 via `Accounts:InvitationHours` / `Accounts__InvitationHours`.

## Upgrade an existing installation

**Stop all writes before applying the new schema, and keep one API instance.** Do not leave the old backend running against the upgraded database. Coordinate frontend and backend: old clients cannot use the new account and cookbook routes.

1. Record old backend/frontend image or commit versions, configuration, `DataDirectory` (absolute path), Compose project name and volume names. Preserve optional provider credentials through the existing secret-management mechanism. Remove obsolete owner-hash configuration after confirming the backup exists.
2. Stop the API, then copy the **entire** data directory, including SQLite journal/WAL files and `keys/`, into a fresh protected backup location. Copy deployment configuration separately. Example for the existing Compose service:

   ```sh
   mkdir -p backups
   docker compose stop api
   docker compose cp api:/app/data backups/pre-accounts-data
   chmod -R go-rwx backups
   ```

   Copy `.env`/secret configuration and Caddy/Compose files into protected backup storage; do not commit them. Do not copy just a live `recipes.db`.
3. Build the new backend and frontend. Keep the old images/publish directories for rollback. Retain existing Compose project name and `recipe_data:/app/data`; never run `down -v`. Configure `Accounts__PublicAppUrl` to your trusted HTTPS origin. The updated Compose derives it from configured `RECIPE_ATLAS_DOMAIN`, not incoming Host headers.
4. Apply the migration and bootstrap **offline**. The bootstrap CLI itself applies pending migrations before prompting:

   ```sh
   docker compose build
   docker compose run --rm --no-deps api --bootstrap-admin
   ```

   With a local SDK and the API stopped, the following is the explicit migration command. Substitute the **actual absolute database path**, not the design-time database:

   ```sh
   dotnet ef database update --project backend/RecipeAtlas.Api --connection 'Data Source=/absolute/existing/data/recipes.db;Foreign Keys=True'
   dotnet run --no-build --project backend/RecipeAtlas.Api -- --bootstrap-admin
   ```

   The CLI and subsequent API must use that same existing `DataDirectory` through their configuration. For a non-default path, export `DataDirectory=/absolute/existing/data` in that shell or configure it in the deployment. Do not accidentally bootstrap another empty directory.
5. `AccountsAndCookbooks` creates a **disabled, passwordless, non-administrator** staging identity and its cookbook. Every previous recipe receives that cookbook ID; old IDs, ingredients, quantities, steps, timestamps and image blobs remain unchanged. The migration does not import the owner hash. `AccountSetups` is empty until bootstrap succeeds, and every `/api` route returns 503 in that intermediate state. `/health` remains a liveness endpoint only.
6. Bootstrap updates the staging identity into the initial administrator, activates it, sets its Identity password, names the existing book `Personal cookbook`, and records completion **in one SQLite transaction**. It does not duplicate data. An interrupted transaction rolls back; retry the same command. If already complete, it exits without reading/replacing credentials. Deliberate administrator recovery is a separate command below.
7. Start the matched application versions:

   ```sh
   docker compose up -d
   ```

   Sign in afresh with the chosen username/password. The old `RecipeAtlas.Session` cookie uses a different scheme/name and cannot grant access. Verify the personal cookbook, recipe counts, representative ordered ingredients/steps, quantity ranges/fractions and photos. Check `/api/auth/me` and `/api/cookbooks` after login; old `/api/recipes/{id}` is retired. Existing image references inside rows are unchanged; response `imageUrl` uses `/api/cookbooks/{cookbookId}/recipes/{id}/image`.

Startup migrations remain the existing deployment policy. No public initial-administrator endpoint or default password is introduced. Deployed migrations should be monitored; future deployment-controlled migrations may remove this startup coupling.

### Verify the schema safely

```sh
dotnet ef migrations list --no-connect --no-build --project backend/RecipeAtlas.Api
dotnet ef migrations has-pending-model-changes --no-build --project backend/RecipeAtlas.Api
```

Historical identifiers: `20261008161847_InitialCreate`, `20261010112715_StringIngredientQuantities`, `20261016082127_AccountsAndCookbooks`, then `20261018100818_CookbookInvitationsAndProfiles`. With `--no-connect`, unknown applied/pending status is expected. The model check does not apply migrations to user data.

Optional read-only SQLite checks against your stopped database/backup: all recipe cookbook IDs are non-null and reference the one initial cookbook; old recipe counts and image bytes agree with the backup; `PRAGMA foreign_key_check` returns no violations; `AccountSetups` contains the singleton after setup. Do not print password hashes, security stamps or invitation hashes during diagnosis.

### Roll back

Do not attempt a schema downgrade: it would lose account/cookbook security relationships. `Down` deliberately refuses. Stop the new API, preserve its entire current data directory as a separate protected backup, and restore the **whole pre-upgrade directory** to the original mount with the API stopped. Preserve old configuration/permissions, then deploy **both previous application versions**. Verify the old data before reopening access. New content/accounts created after upgrade will not exist in that backup; reconcile separately if needed. Do not delete a user database to make an application start.

## Administrator recovery

Requires trusted server access and a maintenance window; stop the API first. This is not ordinary account management:

```sh
dotnet run --no-build --project backend/RecipeAtlas.Api -- --recover-admin
# Container equivalent, same data volume and configuration:
docker compose run --rm --no-deps api --recover-admin
```

Enter an existing administrator username and a new password through the hidden prompt. Recovery resets the framework password, lockout state and security stamp atomically, activates that administrator and invalidates all previous sessions. It does not promote an ordinary user, delete data or create an administrator. Restart the API and sign in afresh. No email reset delivery is implemented.

## Permission matrix

| Action | Owner | Editor | Viewer | Nonmember | Global administrator alone |
|---|---|---|---|---|---|
| Read recipes/images/search in book | Yes | Yes | Yes | No | No |
| Create/update/delete recipes and images | Yes | Yes | No | No | No |
| Edit/delete book and manage members | Yes | No | No | No | No |
| Leave book | No | Yes | Yes | — | According to membership |
| Create own book | Any active account | Any active account | Any active account | Any active account | Yes |
| List accounts/invite/revoke/deactivate ordinary accounts | Only if administrator | Only if administrator | Only if administrator | Only if administrator | Yes |

`Cookbook.OwnerUserId` is the only ownership authority. Memberships store only Viewer/Editor. There is no owner transfer, account deletion or promotion API. Owners can delete their cookbook and all its recipes/images after an explicit UI confirmation. Adding members uses an existing exact username, trimmed and normalized case-insensitively with Identity, without a public account directory. Invitation redemption creates an ordinary account and one personal cookbook; it grants no membership in other books.

Deactivating an owner preserves their book/content/ownership. Active members retain their existing roles. Reactivation restores owner access only after a new login; old cookies remain invalid. Password change invalidates other sessions while refreshing the caller's cookie. Account activity/security stamps are checked every protected request, not after a long validation interval.

All recipe, image and membership operations use `CookbookAccess`. No role lists live in cookies. Writes begin SQLite Serializable (`BEGIN IMMEDIATE`) **before** checking active state and membership, then mutate and commit under the same transaction. A permission change committed before a write is checked must be honoured. An already authorized write transaction may commit first; a concurrent downgrade waits and then takes effect. Invitation redemption/revocation use the same ordering: first committed transaction wins, terminal state prevents the other transition. This is not general optimistic recipe-conflict handling.

Every private API response is `no-store`. Conditional image requests go through authorization and do not return an unauthorised 304. No public blob path is introduced. Same-origin cookies, no CORS, strict SameSite and `X-RecipeAtlas: 1` protect writes.

## Two-user demonstration

1. Complete bootstrap and start both applications as above. Open the frontend in a normal browser and sign in as the administrator. Choose **Accounts Manager → Create invitation → Copy link**. The UI warns that anyone with the link can create one ordinary account. Share privately; existing links cannot be recovered. Opening the link only validates it.
2. Open the invitation in a separate private browser session. Choose a distinct username, display name, contact email and password; submit, then sign in. The new user sees their personal cookbook only. They cannot access Accounts administration or the administrator's private recipes by guessing IDs.
3. In the first browser, open the administrator's cookbook and **Members and settings**. Enter the new user's exact username and choose Viewer. In the second browser return to **Cookbooks**, accept the pending cookbook invitation, then open the shared cookbook and read a recipe/photo. Recipe edit/create/import controls are hidden; direct writes return 403.
4. Change that member to Editor in the first browser. Reopen the book in the second: create/edit/photo actions are available; imports are inside **Add recipe → Import recipe**. An import produces a draft; saving rechecks current book permissions. Account invitations and cookbook sharing are separate operations.
5. Open an editor in the second browser and make an unsaved change. In the first, downgrade/remove membership. Saving is denied with 403/404 and the editor keeps unsaved fields with a clear copy-before-leaving message. Later navigation to that inaccessible cookbook returns to the overview. Deactivate/reactivate the ordinary account under Accounts Manager and verify a fresh login is required.
6. Create a private cookbook in the second browser; the global administrator cannot read it without membership. Sign out/account switch and verify drafts, cookbook selection, recipe cards and photos do not carry across accounts. Browser cache/history cannot be remotely erased; server content remains protected.

These are manual demonstration steps, not a claim of performed browser verification.

## Tests and operational limits

`dotnet run --no-build --project tests/Recipoke.Tests` runs domain/SQLite/architecture and deterministic write-order/rollback checks. `python3 tests/accounts_integration.py` exercises multiple roles, invitations, concurrency, sessions, cache/CSRF and account lifecycle over HTTP with disposable SQLite. `python3 tests/upgrade_compatibility.py /path/to/old-publish/Recipoke.Api.dll` uses the real old binary to create representative data and an actual owner cookie, then upgrades in isolation. GitHub Actions always runs the migration test in the console suite; the optional old-binary comparison needs a supplied publish directory and is not a silently skipped CI check.

Frontend TestBed/Vitest covers invitation terminal states, administration visibility, navigation, role controls, 401/403/404, preserved edits, session cleanup and stale response suppression. No normal test needs external sites, model credentials or paid calls. An optional real Chromium runner in the frontend, `node tests/browser-accounts-smoke.mjs /absolute/backend/root`, starts both applications with disposable data and verifies the invitation/sharing/editor/account workflows plus horizontal layout at 320px, 390px and 1280px. It requires installed Chromium, Node 24+, frontend dependencies and a built backend on PATH through dotnet. The initial UI suite passed 35 checks with Chromium 154; the current 48-check suite also measures Material-icon centering and member-field spacing. It does not require private-repository credentials, but does need both local checkouts, so it is explicitly optional rather than a frontend-only CI step. Public TLS, Docker runtime and a full visual review still require deployment verification.

Shared login/invitation rate budgets suit a small single-instance installation but permit denial of service against other users. Revisit with trustworthy proxy/IP attribution and growing traffic. Sessions and permissions are checked from SQLite; this deliberately adds reads. SQLite serializes writes, including password hashing in the invitation transaction. Watch contention before increasing scale. SameSite Strict may require explicit navigation into the app from external links; invitation validation remains public.

This establishes the cookbook authorization boundary for future shopping lists/cooking sessions. Those features, offline synchronization, ownership transfer, password-reset emails and a general permission engine are not implemented.

## Updating an existing accounts-and-cookbooks installation

Back up the complete configured data directory, stop writes, deploy the matched versions, and restart one backend instance. Startup applies the additive UI/profile migration. Keep the same DataDirectory, key ring and volumes; do not rerun bootstrap or delete a database. Existing accounts, sessions and accepted memberships continue to work. Cover/avatar tables start empty and descriptions/email may be null for existing records. Complete your email and display name under **Account**; no password reset or new login scheme is introduced.

Cookbook creation is now **Cookbooks → Create cookbook**, with optional cover/description and staged member invitations. Its member invitations become visible to the recipient only after creation. A failed cover upload retains the created ID for retry. Accepted members retain their Viewer/Editor role; pending invitations grant only the specific preview metadata/cover, not recipe access. The inbox shows at most five and refills after accept/decline. Owners can cancel pending invites; declined people may be invited again. Account invitations still create accounts separately and now require the recipient's contact email.

On mobile, use the left menu for Cookbooks, Account, administrator-only Accounts Manager, Settings and Sign out. The right back action stays inside the app and uses existing unsaved guards. Recipe import moved from the list to **Add recipe → Import recipe**. The lower Create/Add actions are fixed and flush with the screen bottom. Profile/email and picture changes use **Save profile**; password changes remain separate. The Settings route intentionally contains no controls yet.

Deleting a cookbook is a permanent owner-only action. The separate confirmation states the cookbook name, recipe count and loss of related ingredients, steps, images and invitations. Cancelling sends no DELETE. The backend enforces ownership and a single transaction regardless of UI. There is no undo, ownership transfer, account deletion or email notification service.

Additional normal verification: `python3 tests/ui_support_integration.py`. Optional real-binary upgrade: `python3 tests/ui_upgrade.py /path/to/pre-ui-publish/Recipoke.Api.dll`. Both use synthetic disposable data. The frontend browser runner now exercises the new navigation, consent, profiles, creation and confirmed deletion paths as well as 320px/390px/1280px layouts.


## Local troubleshooting: invitation endpoint returns 404

After pulling backend changes, stop the running API and restart with a build:

```sh
dotnet run --project backend/RecipeAtlas.Api --launch-profile http
```

`--no-build` executes the last compiled DLL. Pulling source files does not rebuild it: an older accounts backend returns 404 for `/api/cookbook-invitations` and its member POST immediately grants access. The matching current backend stages a pending invitation instead. Do not use `--no-build` after pulling unless you have explicitly completed `dotnet build` in this checkout. Start the matching frontend with `npm start`; its proxy forwards `/api/**` to port 5080. Stop any other process serving an older backend on that port.

Without signing in, `curl -i http://localhost:5080/api/cookbook-invitations` should return **401**, rather than 404, on a bootstrapped current backend. In the signed-in browser the same request through port 4200 should return 200 and an array (possibly empty). A backend awaiting its initial setup returns 503. These read-only checks do not alter membership or bootstrap an account.

The frontend now reports a missing consent API explicitly and checks its availability before sending either a member invitation or initial cookbook invitations. It does not fall back to immediate membership. Previously accepted members, including people added while an older binary was running, remain accepted. For a deliberate invitation test, the owner must remove that existing membership and then send a new invitation; this update never revokes access automatically.
