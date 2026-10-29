# Shared shopping lists: implementation verification

Date: 2026-10-21. This verifies the online MVP in the working trees, not a deployed service. No commit, push, branch switch, production database, paid AI request or external recipe import was used.

## Repositories and scope

| Repository | Absolute path | Branch | Unchanged HEAD |
|---|---|---|---|
| Backend | `D:\Programmier Umgebung\RecipeAtlas` | `feature/shared-shoppinglist` | `5e798cfd4ffea616d36cca884e9e3b9fac6c6130` |
| Frontend | `D:\Programmier Umgebung\RecipeAtlasFrontend` | `feature/shared-shoppinglist` | `dcac417973ee6fee6f38b40d7a5c5be997c7580b` |

Both trees were clean at implementation start; no AGENTS.md was present in either repository or applicable parent. All implementation changes remain uncommitted. The frontend is a sibling checkout, not the similarly named nested writable-root path; writes there used the authorized escalation mechanism.

Implemented independent Owner/Member lists, existing-account invitations, ownership transfer, preview cards, text-only items, Enter/blur autosave, checked section, removal/Undo, version checks, durable retries and live invalidations. The user's explicit **15 seconds without renewal** decision overrides an erroneous early plan sentence; the renewal endpoint/timer were removed and fixed-expiry tests executed. Recipe import, quantities, offline/PWA, reload recovery, ordering and bulk clear remain deferred.

## Executed backend and contract evidence

Commands ran from the backend root unless stated otherwise. `python` below denotes the installed `C:/Users/Marco/AppData/Local/Python/pythoncore-3.14-64/python.exe`; frontend scripts use Node 24.21.0/npm 11.19.0. SDK 10.0.401 and EF 10.0.12 match the repository pins.

| Command | Exit/result |
|---|---|
| `dotnet build --no-restore` | 0; zero errors, one NU1900 vulnerability-feed warning |
| `dotnet run --no-build --project tests/Recipoke.Tests` | 0; **703 checks**, 13,529 signature/IL architecture dependencies examined |
| `python tests/shopping_integration.py` | 0; **73 HTTP checks** with disposable SQLite and separate cookie stores |
| `node tests/shopping_events.mjs ../RecipeAtlasFrontend` | 0; **8 live SignalR checks + 29 HTTP checks** |
| `python tests/integration.py` | 0; **63 existing HTTP checks** |
| `python tests/test_sqlite_cleanup.py` | 0; **3 connection-lifetime tests**, invoking **348 existing HTTP assertions**: 159 accounts, 111 concurrency, 78 UI support |
| `python scripts/publish_pre_concurrency.py "$env:TEMP/recipoke-shopping-baseline-audit"` | 0; pinned historical binary published from Git archive without checkout/worktree |
| `python tests/concurrency_upgrade.py <historical Recipoke.Api.dll>` | 0; populated previous database, old columns/relationships/keys/cookies preserved |
| `python tests/shared_editing_upgrade_audit.py <historical Recipoke.Api.dll>` | 0; **3 upgrade scenarios + 28 HTTP checks**, migrated tokens accept a save and reject stale updates |
| `dotnet ef migrations has-pending-model-changes --no-build --project backend/RecipeAtlas.Api` | 0; no model changes since migration |
| `python scripts/export_openapi.py` and final `--check` | 0; generated snapshot matches; Development exposes it and Production returns unavailable |
| `python scripts/sync_frontend_contracts.py ../RecipeAtlasFrontend` and final `--check` | 0; snapshots and quantity fixtures synchronized |
| `python tests/check_shared_contracts.py ../RecipeAtlasFrontend` | 0; OpenAPI and quantity/unit sources match |
| `python tests/test_contract_paths.py` | 0; **5 tests** |
| Frontend: `npm.cmd run check:contracts` | 0; **266 transport type/nullability checks**, routes, write header and quantity fingerprints |

HTTP runs explicitly set an absolute temporary DataDirectory before automatic migrations/bootstrap. No existing development database was opened. Separate-context SQLite tests use barriers and intercepted failures, not arbitrary sleeps, for conflicting operations. Startup/DOM readiness polls are limited to readiness checks. The console suite is executed directly; no zero-test `dotnet test` result is used as evidence.

The historical upgrade checks compare every existing column in populated recipes, ingredients, ordered steps, memberships, string quantities and image blobs, plus key files and usable sessions. They assert unchanged historical migration records followed by exactly `OptimisticAggregateVersions` and `SharedShoppingLists`. The new migration's downward path is separately rejected on a populated database without deleting its content.

## Requirement evidence

| Requirement | Classification and evidence |
|---|---|
| Independent membership, invitations, owner transfer/leave, no administrator bypass | Executed: ShoppingTests, shopping HTTP, invitation/acceptance browser path |
| Independent item changes and atomic same-item version checks | Executed: SQLite barrier races and HTTP races; administrative version unchanged by item writes |
| Fixed 15-second protection, same-token retry does not renew, old tokens cannot revive/release newer protection | Executed: controlled-clock SQLite and frontend state tests; live two-phone protection |
| Remove/Undo preserves identity, five-second deadline, same-actor policy | Executed: SQLite/HTTP, browser removal and Undo |
| Operation receipts atomic with effects, changed-payload rejection, replay does not reapply old writes | Executed: injected rollback, concurrent retry, check-after-uncheck/delete-after-Undo, process restart HTTP |
| Lost permissions checked before receipt replay; guessed IDs cannot cross list boundaries | Executed: HTTP/SQLite removal, unrelated user/admin and cross-list item tests |
| Current account/session validation for live subscriptions | Executed: SignalR removal, deactivation, reconnect and forbidden Watch checks |
| Whole-list delete overrides active editing without late recreation | Executed: HTTP delayed-create rejection and two-browser retained draft after confirmed owner deletion |
| Required booleans and version preconditions | Executed: missing boolean regression, missing/stale versions, wildcard rejection; OpenAPI/type checker |
| Domain/persistence/transport separation | Executed architecture dependency checks plus independent source review; domain has no ASP.NET/EF/HTTP references |
| Application owns transactions and authorization | Executed transaction interceptor and rollback tests plus review of all shopping operations |
| Feature-local frontend state and HTTP service boundary | Source review plus state/component tests and browser checks |
| Full frontend suite/build and final independent review | Executed: 240 frontend tests, production build and contract checks pass; no remaining blocking review finding |

## Browser checks actually performed

`node tests/shopping_browser.mjs ../RecipeAtlasFrontend 'C:/Program Files/Google/Chrome/Application/chrome.exe'` passed **14 checks** in real headless Chromium with two independent cookie contexts at **390 by 844**. It exercised list creation, invitation and acceptance through the UI, live add visibility, reservation disabling another person's checkbox/removal, blur autosave, checking across phones with Space and retained focus, removal and Undo, horizontal overflow, keyboard Tab reachability, dialog focus, cancellation preserving the list, confirmed owner departure despite its own AccessLost event, and the other phone retaining unsaved text after whole-list deletion.

The generated [mobile screenshot](screenshots/shopping-list-mobile.png) was opened and visually inspected: readable text, visible purchased section, reachable quick-add/settings, no horizontal overflow. This is a sample mobile layout, not physical-device or exhaustive accessibility testing. Initial harness failures were corrected to await an editable (not merely enabled) input and completed dialog focus transition; no application assertions were removed.

## Review corrections

- Missing invitation/check-state booleans initially defaulted to false. The HTTP regression observed 200 instead of 400 before `JsonRequired`; the final test verifies 400 and unchanged state.
- Access loss during an in-flight request could retain a permanent navigation lock. Interrupted requests now become uncertain, and an actor's own successful leave/delete acknowledgement can complete after AccessLost without restoring private content.
- A retained draft for a remotely removed item needed an explicit discard action before other text editing could resume.
- Historical rename receipts must not pair an old name with a newer snapshot version. The component now uses an authorized coherent snapshot.
- Undo must be usable after a successful deletion receipt even if its follow-up GET is slow. It uses the immutable deletion operation ID independently of that read.
- Moving an item between purchased sections destroys the original checkbox DOM node. Focus moves to an enabled control while the request is pending.
- A denied overview GET could recursively refresh. It now clears private snapshots, stops loading and blocks further requests; individual list invalidations still refresh the remaining overview.

Each correction has a focused regression. Independent review covered connected backend authorization/transaction/session code and frontend state/service/live delivery, not only file locations or test names.

## Limits and operating risks

One API instance is supported: subscriptions are in memory and notifications are best-effort; reconnect/focus/visible 30-second refresh repairs missed messages. Receipts persist for the list lifetime and can retain a historical item text. This is documented retry behavior, not an offline feed or a comprehensive data-retention policy. Whole-list deletion is permanent and ignores reservations by design. The 15-second expiry can interrupt a long edit; text is retained and an explicit reviewed reacquisition is required.

NU1900 means NuGet vulnerability metadata could not be fetched; it does not mean packages were declared vulnerability-free. Existing frontend Vite/Angular build tooling advisories remain (one high, one moderate); no unrelated forced upgrades were performed. Python 3.14 emitted existing HTTPError ResourceWarnings during the older suites; their explicit SQLite lifetime checks passed.

No hosted CI run, reverse-proxy/TLS deployment, physical iOS/Android device, exhaustive long-list layout, external import provider or offline behavior was verified. No service worker or durable client queue was introduced.

## Final frontend results and workspace state

The frontend implementer executed the final commands from `D:\Programmier Umgebung\RecipeAtlasFrontend` after the last correction:

| Command | Result |
|---|---|
| `npm.cmd test` | Exit 0; **240 tests in 21 files**, including 29 new shopping tests across four files |
| `npm.cmd run build` | Exit 0; initial bundle **758.41 kB**, lazy shopping bundle **104.72 kB**, no budget warnings |
| `npm.cmd run check:contracts` | Exit 0; **266 checks** |

Independent review reported no remaining blocking defect, including a final scoped review of the denied-overview fix. Both repositories' `git diff --check` returned 0; Git's LF/CRLF notices are not whitespace failures. Both branch names and HEAD hashes above were rechecked and unchanged.

The final process check found no feature-test API, Angular server, Chromium profile or Python/Node test process left running. Fixture cleanup removed disposable databases, logs and browser profiles. The explicitly verified temporary historical publish directory was also removed. Existing user browser/editor processes were left untouched. Build outputs/dependency installations remain in their normal ignored locations; the screenshot is an intentional verification artifact.

The implementation is ready for the next development step and manual acceptance testing. No blocking defect remains in the executed/reviewed scope; the deployment and coverage limits above still apply. Nothing was committed or pushed.

### Files changed and final Git status

All changes below belong to this implementation. `M` means an existing tracked file changed; `??` means a new, untracked file. No file is staged.

Backend:

```text
 M .github/workflows/verify.yml
 M README.md
 M backend/RecipeAtlas.Api/Common/Http/ApiDescription.cs
 M backend/RecipeAtlas.Api/Infrastructure/Persistence/Migrations/RecipeDbModelSnapshot.cs
 M backend/RecipeAtlas.Api/Infrastructure/Persistence/RecipeDb.cs
 M backend/RecipeAtlas.Api/Program.cs
 M contracts/recipoke.openapi.json
 M docs/architecture/README.md
 M tests/Recipoke.Tests/Program.cs
 M tests/concurrency_upgrade.py
 M tests/shared_editing_upgrade_audit.py
?? backend/RecipeAtlas.Api/Features/Shopping/Api/ShoppingEndpoints.cs
?? backend/RecipeAtlas.Api/Features/Shopping/Application/ShoppingAccess.cs
?? backend/RecipeAtlas.Api/Features/Shopping/Application/ShoppingContracts.cs
?? backend/RecipeAtlas.Api/Features/Shopping/Application/ShoppingItems.cs
?? backend/RecipeAtlas.Api/Features/Shopping/Application/ShoppingLists.cs
?? backend/RecipeAtlas.Api/Features/Shopping/Application/ShoppingOperations.cs
?? backend/RecipeAtlas.Api/Features/Shopping/Application/ShoppingReads.cs
?? backend/RecipeAtlas.Api/Features/Shopping/Application/ShoppingReservations.cs
?? backend/RecipeAtlas.Api/Features/Shopping/Infrastructure/ShoppingConnections.cs
?? backend/RecipeAtlas.Api/Features/Shopping/Infrastructure/ShoppingHub.cs
?? backend/RecipeAtlas.Api/Features/Shopping/Infrastructure/ShoppingNotifications.cs
?? backend/RecipeAtlas.Api/Features/Shopping/Infrastructure/ShoppingRealtime.cs
?? backend/RecipeAtlas.Api/Features/Shopping/ShoppingRegistration.cs
?? backend/RecipeAtlas.Api/Infrastructure/Persistence/Configurations/ShoppingConfiguration.cs
?? backend/RecipeAtlas.Api/Infrastructure/Persistence/Migrations/20261021125240_SharedShoppingLists.Designer.cs
?? backend/RecipeAtlas.Api/Infrastructure/Persistence/Migrations/20261021125240_SharedShoppingLists.cs
?? backend/Recipoke.Domain/Shopping/ShoppingItem.cs
?? backend/Recipoke.Domain/Shopping/ShoppingList.cs
?? docs/architecture/adr/015-shared-shopping-lists.md
?? docs/shared-shopping-lists.md
?? docs/superpowers/plans/2026-10-21-shared-shoppinglist.md
?? docs/superpowers/specs/2026-10-21-shared-shoppinglist-design.md
?? docs/verification/screenshots/shopping-list-mobile.png
?? docs/verification/shared-shoppinglist.md
?? tests/Recipoke.Tests/ShoppingTests.cs
?? tests/shopping_browser.mjs
?? tests/shopping_events.mjs
?? tests/shopping_fixture.mjs
?? tests/shopping_integration.py
```

Frontend:

```text
 M README.md
 M contracts/recipoke.openapi.json
 M docs/architecture.md
 M package-lock.json
 M package.json
 M scripts/check-contracts.mjs
 M src/app/app-routing.module.ts
 M src/app/app.component.ts
 M src/app/core/guards/cookbook-route-reuse.strategy.ts
?? src/app/core/models/shopping.model.ts
?? src/app/core/services/shopping.service.spec.ts
?? src/app/core/services/shopping.service.ts
?? src/app/features/shopping/pages/shopping-detail/shopping-detail.component.html
?? src/app/features/shopping/pages/shopping-detail/shopping-detail.component.scss
?? src/app/features/shopping/pages/shopping-detail/shopping-detail.component.ts
?? src/app/features/shopping/pages/shopping-overview/shopping-overview.component.html
?? src/app/features/shopping/pages/shopping-overview/shopping-overview.component.scss
?? src/app/features/shopping/pages/shopping-overview/shopping-overview.component.ts
?? src/app/features/shopping/shopping-events.spec.ts
?? src/app/features/shopping/shopping-events.ts
?? src/app/features/shopping/shopping-state.spec.ts
?? src/app/features/shopping/shopping-state.ts
?? src/app/features/shopping/shopping.module.ts
?? src/app/features/shopping/shopping.spec.ts
?? src/app/features/shopping/styles/_shopping-layout.scss
```
