# Shared-editing verification audit

> Follow-up: the user authorized repairs after this audit. Findings F1–F4 have been repaired in the working trees; see [fixes and fresh verification](shared-editing-fixes.md). The report below preserves the pre-fix findings, counts and Git state at the audited commits. The retained regression tests now pass.

Date: 2026-10-21. Platform: Windows 10 x64, Europe/Berlin. Verification only; production source, existing assertions, dependency versions and lockfiles were left unchanged.

## Summary and readiness

**Not ready for shared-editing sign-off or acceptance as the completed foundation for the next development step.** Two P2 editor defects need repair: conflict review drops local ingredient draft/review state, and session expiry during initial recipe loading leaves a permanent spinner. Three retained frontend tests reproduce these defects. Two P3 Windows verification-tool defects also reproduce; neither demonstrates a backend data-integrity defect.

Executed evidence:

- Backend build succeeds; **627 console checks**, **411 existing HTTP checks**, **73 additional shared-editing HTTP checks**, and **28 additional upgrade HTTP checks** pass. Counts are checks/asserted requests, not 1,139 distinct test cases.
- Frontend production build and **191 contract checks** pass. Final frontend run: **202 existing tests pass; 3 added audit tests fail**, across 17 files.
- Real historical-binary upgrade preserves old columns, memberships, quantity strings, ordering, image bytes, keys and existing sessions. Migrated recipe and cookbook versions then accept a save and reject a stale retry.
- Existing browser smoke passes **78 checks** with an audit-only Windows launcher. Extended run passes **111 checks**, including 390 × 844, 50 ingredients, 50 steps, keyboard focus and reachable dialog actions. These runs overlap; their counts must not be added as unique coverage.
- The documented cross-repository sync check fails on matching contracts under Windows. One retained Python regression test demonstrates it.

No executed check found a lost update, unauthorized content access, invitation double redemption, or migration data loss. This statement is bounded by the executed scenarios below; it does not certify every interleaving or deployment.

## Repositories and working boundaries

| Repository | Absolute path | Branch | Audited HEAD |
|---|---|---|---|
| Backend | `D:\Programmier Umgebung\RecipeAtlas` | `feature/shared-editing` | `1edf4ef4e00224516d3e52a8bae1e838db86f69f` |
| Frontend | `D:\Programmier Umgebung\RecipeAtlasFrontend` | `feature/shared-editing` | `1b5db9b9e74ebe9fa516b4dc3da5305eb008fb9a` |

The frontend is the requested sibling `../RecipeAtlasFrontend`, rather than a nested checkout. Before verification, root/parent instruction locations and both trees were searched for `AGENTS.md`; none was found. A later hidden/no-ignore search confirmed this outside generated dependency/build directories.

Initial backend status: `?? recipoke-data-backup-20261018-193101/`. Initial frontend status: clean. The backup directory was preserved and its database contents were not inspected. No branch switch, pull, merge, rebase, worktree, commit, or push was performed. Git initially rejected sandbox ownership; subsequent read-only commands supplied a per-command `safe.directory` exception. No global Git configuration was changed.

## Tools, versions and method

Installed skills read: Superpowers using-superpowers and Codex tool adaptation, verification-before-completion, systematic-debugging, requesting-code-review, dispatching-parallel-agents; Ponytail and Ponytail audit; Context7. Independent read-only frontend and account/architecture reviews were used, alongside focused test work. Product brainstorming and worktree workflows were intentionally excluded by the audit instructions. Ponytail is available as skill guidance; no Ponytail-specific command/tool was assumed.

Actual versions: .NET SDK **10.0.401**, runtime/EF Core/Microsoft.Data.Sqlite **10.0.12**, local dotnet-ef **10.0.12**, Node **24.21.0**, npm **11.19.0**, Python **3.14.8**, Angular core **22.2.1**, CLI/build **22.2.2**, Vitest **4.1.11**. Node satisfies the package's `^24.15.0` engine range; CI uses 24.19.0. The installed .NET SDK matches README/CI. README line 11 refers to `global.json`, but that file is absent from the checkout and tracked file list; SDK selection is therefore not repository-pinned as that sentence suggests.

The `python` and `py` App Execution Aliases were inaccessible inside the sandbox. The installed interpreter at `C:\Users\Marco\AppData\Local\Python\pythoncore-3.14-64\python.exe` worked. PowerShell execution policy blocks `npm.ps1`; `npm.cmd` works. Existing installed frontend dependencies were sufficient; no npm install or lockfile rewrite was needed.

Context7 was available. Its initial Microsoft.Data.Sqlite resolution returned unrelated SQLite libraries; no claim was based on those. Resolving EF Core selected `/dotnet/entityframework.docs`. Queries covered concurrency-token predicates, affected-row failures and SQLite contention. It did not expose a 10.0.12-specific documentation version or answer the precise provider transaction-default question. General framework explanations were used only as context; application claims rely on the installed 10.0.12 code/configuration and real SQLite tests. Official references: [EF concurrency](https://github.com/dotnet/EntityFramework.Docs/blob/main/entity-framework/core/saving/concurrency.md), [ExecuteUpdate/Delete concurrency limitations](https://github.com/dotnet/EntityFramework.Docs/blob/main/entity-framework/core/saving/execute-insert-update-delete.md). Documentation is not counted as observed application behavior.

Failure investigation preserved the failing input/assertions, traced the responsible code, and reran narrow reproductions. No application fix was attempted. Angular NG0956 tracking warnings appeared in the added review tests; the assertions fail on state loss, not on those warnings.

## Specification established

Read sources: both READMEs; backend `docs/architecture/README.md`, ADRs 001–014, `docs/accounts-and-cookbooks.md`, `docs/shared-editing.md`; frontend `docs/architecture.md`; OpenAPI snapshots and contract checkers; CI, Docker/Compose configuration; relevant operation, HTTP, component, service and browser tests. Historical verification counts in those documents were treated as claims to recheck.

| Decision | Recorded specification and implementation |
|---|---|
| Architecture | ADRs 001–003: feature monolith, independent Domain, Application intentionally uses EF directly and owns commits. No requirement for generic repositories or a new mediator. |
| Sessions | ADR 009 supersedes the historical single-owner cookie decision. Identity checks stamp/activity on every request; deactivation/reactivation rotate stamps. Password change renews the caller and invalidates other sessions. |
| Account invitations | ADR 010: administrator-issued/revoked, expiring one-use hash-backed invitation; account creation, personal cookbook and consumption share a transaction. No public ordinary registration. |
| Cookbook access | ADR 011: immutable `OwnerUserId`; membership rows contain Editor/Viewer only. Central `CookbookAccess` plus pure domain permissions. Global administrator capability grants no private content access. |
| Consent and persistence | ADRs 005/012/013: accepted memberships and pending invitations differ; images are private SQLite blobs; retained data/key paths; startup applies migrations. Existing membership is not converted to pending consent. |
| Aggregate versions | ADR 014: independent opaque nonempty GUID versions. Recipe includes ordered ingredients/steps and photo. Cookbook includes metadata, cover, accepted memberships and pending invitation lifecycle. Recipe changes do not advance cookbook version. |
| Atomicity and permissions | Write operations reserve the SQLite writer before current authorization, then commit aggregate children and version together. Failed EF CAS rolls back/clears tracking and rechecks current access. No content retry. |
| Reads | `ReadSnapshot` provides a deferred read transaction for compound representations. Content and version must belong to one snapshot. |
| Frontend conflict state | Feature/component-scoped baseline and fetched latest stay separate until explicit review. Whole ingredients/steps sections are selected; applying does not save. Discarding local work needs confirmation. |
| Photo sequence | Text/details and photo are separate requests. Photo uses the preceding successful response version; partial success retains the successful ID/version and pending image. Retry does not replay unchanged text. |
| Deployment | Coordinated backend/frontend release and tab reload; additive concurrency migration; stop writes and back up data/keys; no old writer running alongside the new one. Down deliberately refuses unsafe schema rollback. |

ADR 014 explicitly selects strong `If-Match` for **binary photo PUT as well as DELETE**. This is a coherent extension of the planned default: a raw binary body has no JSON `expectedVersion` slot. Response changes are documented: image mutation returns 200 with new version; cookbook update/cover returns representation; member mutations return version; successful leave/self-removal returns 204 without protected content. No material undocumented concurrency-contract alternative was found.

## Architecture inspection

The inspected implementation respects the recorded architecture. No concrete architecture violation was established.

- `backend/Recipoke.Domain/Recipoke.Domain.csproj:1` has no ASP.NET/EF package reference. Actual assembly/signature/IL inspection traversed **9,658 dependencies**; Domain/endpoint/Application restrictions and a scanner negative probe passed. This examines dependencies rather than directory labels.
- `Features/Recipes/Api/RecipeEndpoints.cs:5` binds IDs/bodies/headers and maps responses; operations own queries, authorization, mutation and transactions. Cookbook/account endpoint paths follow the same separation.
- `Features/Recipes/Application/UpdateRecipe.cs:11` starts its transaction before authorization and owns commit. Create/delete/image, cookbook metadata/member/invitation/cover/delete operations were also traced. EF in Application is intentional under ADR 003.
- EF mappings are in `Infrastructure/Persistence/Configurations/RecipeConfiguration.cs:12` and `Infrastructure/Persistence/RecipeDb.cs:23`. `ConcurrencyWrites.cs:11` sets OriginalValue/current Version; `SaveAsync:18` handles only EF concurrency failure, rollback, tracking reset and fresh access. Domain contains the version property without HTTP/EF concerns.
- Cookbook permissions are centralized in `Features/Cookbooks/Application/CookbookAccess.cs:6` and Domain `CookbookPermissions`; active-account checks are shared. Nested resource queries also constrain CookbookId.
- Frontend requests use `core/services/recipe-api.service.ts`, `cookbook.service.ts` and established account/auth services. The component-scoped recipe coordinator and cookbook page own conflict state; the shared conflict dialog only presents choices. No global conflict store or duplicate token mechanism was found.
- Account invitation uniqueness, image URL cache-busting identifiers and Identity security stamps have separate purposes; they are not competing aggregate concurrency mechanisms.

## Isolation and database safety

Before starting an API, `Program.cs:21` and factory/appsettings/test startup paths were inspected. Runtime persistence is `DataDirectory/recipes.db`; keys are `DataDirectory/keys`. The default is under the API content root. Startup always invokes `MigrateAsync`, so an unqualified local start was unsafe for this audit.

Every test/bootstrap/browser API used an explicit newly-created absolute temporary `DataDirectory`, random available loopback port, Development environment and separate CookieJar/browser contexts. Upgrade runs deliberately reused only their own temporary directory across historical/current binaries. Environment configuration overrides appsettings/user-secrets; command arguments did not supply a competing data path. `RecipeText__ApiKey` was blanked; no paid model or live external import was used. EF inspection used `--no-connect` and the design-time factory; no user database was selected.

Across executed suites the actors include two recipe writers, Viewer, unrelated user, a global administrator outside another owner's cookbook, and two independent owner sessions. Both browser contexts were isolated. Passwords were random stdin inputs, invitations were parsed in memory, and secrets/cookies were not printed.

## Commands and results

In the table, **B** means the absolute backend path above, **F** the frontend path, and **PY** means `& 'C:\Users\Marco\AppData\Local\Python\pythoncore-3.14-64\python.exe'` in PowerShell. Environment settings for the special commands are listed afterward. Commands were run against the current branches; no `dotnet test` result was substituted for the console harness.

| Command | Directory | Exit/result |
|---|---|---|
| `dotnet --info`; `node --version`; `npm.cmd --version`; installed Python `--version` | B | Versions above; aliases/wrapper issues separately recorded. |
| `dotnet restore --locked-mode` | B | 0. Cached packages restore; NU1900 vulnerability-feed warnings. Escalated retry also 0 with the same cached warnings. |
| `dotnet build --no-restore` | B | 0; 0 errors, 2 NU1900 warnings. |
| `dotnet run --no-build --project tests/Recipoke.Tests` | B | 0; **627 checks**, including real SQLite, fault-injected rollback and architecture scan. |
| `PY tests/integration.py` | B | 0; **63 HTTP checks**, persistence across restart. |
| `PY tests/test_sqlite_cleanup.py` | B | First sandbox attempt 1: bootstrap/startup access failures and failed temp deletion, not application assertions. Escalated execution 0: **3 lifetime tests**, running **159 account + 111 concurrency + 78 UI-support HTTP checks**. Python emitted HTTPError resource warnings; assertions/disposal checks passed. |
| `PY tests/shared_editing_audit.py` | B | Sandbox bootstrap blocked; escalated execution 0: **10 scenarios / 73 HTTP checks**, including same-recipe and redemption barriers. |
| `dotnet tool restore` | B | 0; dotnet-ef 10.0.12. |
| `dotnet ef migrations list --no-connect --no-build --project backend/RecipeAtlas.Api` | B | 0; **5 migrations**. Applied/pending database status deliberately unavailable with no connection. |
| `dotnet ef migrations has-pending-model-changes --no-build --project backend/RecipeAtlas.Api` | B | 0; **no model changes**. NU1900 cached warnings. |
| `PY scripts/publish_pre_concurrency.py 'D:\Programmier Umgebung\RecipeAtlas\docs\verification\.audit-baseline'` | B | 0; Git archive of pinned baseline, locked restore and publish. No checkout/worktree. |
| `PY tests/concurrency_upgrade.py 'D:\Programmier Umgebung\RecipeAtlas\docs\verification\.audit-baseline\Recipoke.Api.dll'` | B | 0; exhaustive old-column/session/key preservation and one added migration. Script does not expose an assertion count. |
| `PY tests/shared_editing_upgrade_audit.py docs/verification/.audit-baseline/Recipoke.Api.dll` | B | 0 final run; **3 scenarios / 28 HTTP checks**. Initial new-test fixture assumed current `/members` wrapper on the historical API; corrected to historical list contract without changing application expectations. |
| `PY scripts/export_openapi.py --check` | B | 0; generated snapshot equal, Development exposed, Production returns 404. |
| `PY tests/test_contract_paths.py` | B | 0; **4 tests**, including read-only/error-path behavior. |
| `PY tests/check_shared_contracts.py 'D:\Programmier Umgebung\RecipeAtlasFrontend'` | B | 0; OpenAPI, quantities and unit definitions match semantically. |
| `PY scripts/sync_frontend_contracts.py 'D:\Programmier Umgebung\RecipeAtlasFrontend' --check` | B | **2**; false fingerprint drift on Windows, finding F3. No files written. |
| `PY tests/shared_editing_contract_audit.py 'D:\Programmier Umgebung\RecipeAtlasFrontend'` | B | **1**; **1 failing regression**, demonstrating F3. |
| `npm.cmd run check:contracts` | F | 0; **191 checks**. |
| `npm.cmd run build` | F | Sandbox attempt 1 with no diagnostic after Building. Escalated retry 0: **757.00 kB initial**, unchanged budgets. |
| `npm.cmd test` before added tests | F | 0; **202 tests / 16 files**. |
| `npm.cmd test -- --include=src/app/features/recipes/pages/recipe-editor/shared-editing.audit.spec.ts` | F | **1**; focused final audit spec has **3 failures**, F1/F2. Earlier two-test version also failed as expected. |
| Final `npm.cmd test` | F | **1**; **202 passed, 3 failed / 205 total**, 16 passing files and 1 failing audit file. Five assertion messages represent 3 failed tests, not 5 failed tests. |
| `node tests/browser-accounts-smoke.mjs 'D:\Programmier Umgebung\RecipeAtlas' 'C:\Program Files\Google\Chrome\Application\chrome.exe'` | F | **1** before browser assertions: Windows `spawn ...node_modules\.bin\ng ENOENT`, F4. |
| `node tests/shared_editing_browser_audit.mjs 'D:\Programmier Umgebung\RecipeAtlasFrontend' 'D:\Programmier Umgebung\RecipeAtlas' 'C:\Program Files\Google\Chrome\Application\chrome.exe'` | B | 0; **78 browser checks** using temporary Windows launcher. |
| Same browser audit with `RECIPOKE_AUDIT_MOBILE=1` | B | 0; **111 checks**, longer synthetic lists, 844px height and additional mobile/focus assertions. |
| Final per-repo `git diff --exit-code`, branch, HEAD, status | B/F | Recorded in final-state section; production/previously tracked files unchanged. |

Historical publish used process-local Git config: `GIT_CONFIG_COUNT=1`, `GIT_CONFIG_KEY_0=safe.directory`, `GIT_CONFIG_VALUE_0=D:/Programmier Umgebung/RecipeAtlas`; no global write. Browser runs set `RECIPOKE_SCREENSHOTS=D:\Programmier Umgebung\RecipeAtlas\docs\verification\screenshots`. The mobile variant additionally sets `RECIPOKE_AUDIT_MOBILE=1`.

NuGet vulnerability feed availability remains unverified: cached restore succeeded but could not query `https://api.nuget.org/v3/index.json`. No dependency was upgraded to bypass it. Full old single-owner/account-pre-UI binary upgrades, hosted CI, containers and public TLS were not executed.

## Requirements matrix

Classification: **V = Verified by executed test**, **I = Supported by code inspection only**, **F = Failed**, **B = Blocked**, **N = Not tested**. A V covers the stated scenario, not all hypothetical races. Existing coverage is named separately from additional verification; every V below corresponds to a runner actually executed above.

Evidence shorthand: **Console** = `tests/Recipoke.Tests`; **Accounts** = `accounts_integration.py`; **HTTP-CAS** = `concurrency_integration.py`; **UI-HTTP** = `ui_support_integration.py`; **Audit-HTTP** = new `shared_editing_audit.py`; **Upgrade** = existing `concurrency_upgrade.py`; **Audit-Upgrade** = new upgrade script; **Editor/Cookbooks/Services/Session** = corresponding frontend specs; **Browser** = audit-launched smoke and mobile variant.

| Requirement | Relevant implementation | Existing coverage | Additional verification needed/performed | Result and evidence |
|---|---|---|---|---|
| Framework-independent Domain | Domain project/entities/permissions | ArchitectureTests signatures/IL | Inspect actual references | **V** Console scanner passes; source review corroborates it. |
| Transport-focused endpoints; operation-owned transactions | Recipe/Account/Cookbook endpoints and Application operations | ArchitectureTests, operation tests | Trace full routes through commit | **I** ownership traced; dependency/rollback tests pass but do not alone prove all orchestration boundaries. |
| Persistence-owned mappings and centralized authorization | RecipeConfiguration, RecipeDb, CookbookAccess | ArchitectureTests, Accounts | Enumerate mutation paths | **I** mappings and common rules traced; executed role checks corroborate behavior. |
| Service-layer HTTP and feature-local conflicts | RecipeApiService, CookbookService, coordinator/page | Services, Editor, Cookbooks | Read ownership and network calls | **I** callers/state traced; service and instance-isolation tests corroborate. |
| Immediate session invalidation | ActiveSessionValidation, AccountAccess, AdministratorAccounts | Accounts deactivation/reactivation | Existing suite executed | **V** deactivated cookies rejected; reactivation does not restore them. |
| Password policy and other-session invalidation | AccountAuthentication; caller cookie renewal | Accounts two-session password change | Existing suite executed | **V** caller survives, other old cookie fails. |
| Public registration requires invitation | No register endpoint; Invitations.RedeemAsync | Accounts public route/missing token | Existing suite executed | **V** direct registration unavailable; missing invitation fails. |
| Admin-only issue/revoke | Invitations + AccountAccess | Accounts role checks | Existing suite executed | **V** ordinary user cannot issue/revoke. |
| Expired/revoked/consumed invitation rejected | InvitationRules and transactional redeem | Console, Accounts | Existing suite executed | **V** each status fails safely. |
| Failed registration leaves invitation usable | Invitations transaction | Accounts duplicate/invalid registration | Existing suite executed | **V** failure followed by valid redemption succeeds. |
| Concurrent redemption at most one account | Invitations immediate transaction/unique identity | Accounts races | Audit-HTTP barrier added | **V** separate clients return 204/400; exactly one account, consumed invitation invariant. |
| Owner manages; Editor edits; Viewer cannot write/photo | Domain permissions, CookbookAccess | Accounts, Console | Existing suite executed | **V** correct role/status outcomes. |
| Unrelated user and administrator without membership cannot read private content | CookbookAccess; nested filters | Accounts | Existing suite executed with distinct owner/admin | **V** 404 isolation; administrator capability grants no content access. |
| Wrong cookbook route cannot expose a recipe/image | CookbookId predicates | Accounts nested-ID assertions | Existing suite executed | **V** wrong nested scope rejected. |
| Removed/downgraded members cannot write or fetch conflict detail | Authorization before version; fresh failure recheck | Accounts, HTTP-CAS | Existing suite executed | **V** 403/404 take precedence; no private current state/version in conflict errors. |
| Permission changes ordered against active writes | Serializable before access reads | AccountBoundaryTests deterministic interceptors | Inspect scheduling, not sleeps | **V** waiting downgrade cannot overtake authorized writer; next request denied. |
| Post-rollback authorization is fresh | ConcurrencyWrites rollback/clear/recheck | ConcurrencyTests deactivation interceptor | Existing suite executed | **V** post-rollback deactivation returns 401 instead of conflict. Target-row deletion edge remains **I**, see gaps. |
| Images/conditional responses never bypass auth | Authorized image operations, no-store middleware | Accounts conditional image request; UI-HTTP | Existing suites and route inspection | **V** unauthorized conditional request denied; code review found no unauthenticated 304 path. |
| Two clients load same recipe version | GetRecipe snapshot | HTTP-CAS | Audit-HTTP independent cookies | **V** both loaded identical token. |
| A wins; B stale save fails; A content intact | UpdateRecipe, ConcurrencyWrites | HTTP-CAS, direct EF test | Audit-HTTP barrier on same recipe | **V** simultaneous submissions give 200/409; winner fields/ingredients/steps intact. |
| Failed save has no partial ingredients/steps | One write transaction | RecipeOperationTests injection; ConcurrencyTests | Verify winner children after race | **V** real write fault and stale CAS both preserve complete prior/winner children. |
| Ingredient-only change advances recipe | UpdateRecipe always advances | General update coverage | Audit-HTTP ingredient-only scenario | **V** only ingredient quantity changes; new version and persisted content. |
| Step-only and ordering-only changes advance recipe | Same aggregate update | HTTP-CAS step edit | Audit-HTTP reverse-only ordering | **V** version advances and exact order persists. |
| Photo replacement/removal advance recipe | ReplaceRecipeImage, RemoveRecipeImage | HTTP-CAS; operation tests | Audit-HTTP removal version | **V** response/new GET tokens change; image removal persists. |
| Stale photos preserve current image | CAS around image + parent | HTTP-CAS stale PUT/DELETE | Audit-HTTP stale insertion on imageless recipe | **V** winning bytes intact; failed insert leaves zero image rows. |
| Stale recipe deletion cannot remove newer revision | Versioned tracked delete | HTTP-CAS | Existing suite executed | **V** 412; later GET retains winning revision. |
| Separate recipes do not conflict | Independent Recipe.Version | HTTP-CAS | Existing suite executed | **V** unrelated save leaves first token unchanged. |
| Reviewed retry succeeds; intervening edit conflicts again | Explicit submitted baseline | HTTP-CAS, Editor | Existing tests and browser explicit save | **V** successful latest retry and another stale 409; UI second conflict recoverable. |
| Expected version participates in database write | IsConcurrencyToken; OriginalValue | Direct independent-context ConcurrencyTests | Inspect code rather than rely on name | **V** actual stale EF SaveChanges fails; mappings/OriginalValue corroborate atomic enforcement. |
| All applicable mutation paths use CAS/transaction | Recipe/cookbook/member/invite/cover paths | HTTP-CAS preconditions and invariants | Enumerated source paths | **I** every existing path traced; executed tests cover principal operations but not every possible combination. |
| Compound returned content/version share state | ReadSnapshot; response built in write transaction | Ordinary GET/write response assertions | Forced split-read interleaving not added | **I** transaction design supports coherence; adversarial GET interleaving not separately executed. |
| Concurrent cookbook metadata edits do not overwrite | RenameAsync CAS | HTTP-CAS two owner CookieJars/barrier | Existing suite executed | **V** one 200, one 409. |
| Add/accept/role/remove advance cookbook | Invite/Respond/ChangeMemberAsync | HTTP-CAS role/invite checks | Audit-HTTP add/accept/remove assertions | **V** displayed/returned/new GET versions differ at each transition. |
| Leave advances cookbook and returns no protected body | Leave/ChangeMemberAsync | HTTP-CAS stale/success leave | Audit-HTTP before/after version | **V** 204, token advances for remaining owner; former member GET fails. |
| Membership invalidates stale metadata | Single Cookbook.Version | HTTP-CAS | Existing suite executed | **V** stale metadata and role return 409; stale removal 412. |
| Concurrent memberships preserve uniqueness/ownership | Cookbook version; composite keys; immutable owner | HTTP-CAS member barrier, Accounts | Inspect resulting rows | **V** exactly one command commits; unique users; one owner; no owner mutation. |
| Recipe changes do not advance cookbook | Separate aggregates | HTTP-CAS | Audit-HTTP and Audit-Upgrade | **V** recipe text/order/photo changes leave cookbook token unchanged. |
| Stale cookbook deletion protected | Transaction + versioned final DELETE | HTTP-CAS, UI-HTTP | Existing suite executed | **V** 412 rolls back preceding recipe/member deletes, preserving cover/content. |
| Version in editable representations | Recipe/Cookbook DTOs; members wrapper | Contract checks, HTTP-CAS | Generated OpenAPI and cross-repo compare | **V** runtime and types agree; read-only recipe cards intentionally lack update token. |
| Body expectedVersion; header strong If-Match | VersionPrecondition; endpoints | HTTP-CAS | Existing suite executed | **V** missing required tokens 428; malformed JSON token 400; raw photo PUT follows ADR. |
| Reject wildcard/weak/list/unquoted tokens | VersionPrecondition.Require | HTTP-CAS malformed headers | Existing suite executed | **V** malformed tokens safely rejected; no wildcard overwrite. Empty header is treated as missing (428), quoted empty token malformed (400 by inspection). |
| 409 body / 412 header with stable code | OperationFailure, middleware | HTTP-CAS exact codes | Existing suite executed | **V** `concurrency_conflict`, no embedded current resource/version. |
| New versions returned for continued mutations | Mutation DTOs | HTTP-CAS; service specs | Audit-HTTP/Upgrade use returned tokens | **V** clients continue with returned token; leave intentionally 204. |
| OpenAPI and frontend models match actual API | ApiDescription and snapshots | Frontend compiler contract checker | Runtime export + both repo semantic checks | **V** generation/191 type checks/semantic equality; fingerprint CLI **F**, F3. |
| Recipe then photo uses returned version | Coordinator.save, photos.persist | Services; Editor partial-save tests | Test reads/assertions inspected | **V** component/service tests capture second call's new version. |
| Photo failure after text shows partial success | Coordinator.partialSaved; image local state | Editor tests injecting 412/network/access/expiry | Existing specs executed | **V** committed text distinguished, pending blob preserved; no unchanged text replay. Full browser fault injection **N**. |
| Failed photo leaves no orphan data | Shared DB transaction, cascades | Console/HTTP-CAS | Audit-HTTP stale initial image insertion | **V** no blob after failed insertion; winning image remains after stale replacement. |
| Conflict preserves form values, ingredients/steps, pending photo and route | Separate baseline/latest | Editor controls/blob/no-navigation tests | Browser stale save and added state tests | **F** F1 drops pending/review state. Ordinary form/control/blob/navigation scenarios pass. |
| Local and server comparison readable | ConflictReviewComponent | Editor + browser smoke | 390×844 long-list browser variant and screenshots | **V** stacked sources, long sections, scroll and reachable buttons. |
| Confirm before discarding; apply does not auto-save | Review/discard coordinator methods | Editor, Cookbooks; browser cancel/apply | Existing suite executed | **V** confirmation cancellation preserves work; apply stays dirty until explicit save. |
| Next save uses reviewed version; second conflict recoverable | baseline/latest explicit adoption | Editor version-capture/repeated conflict | Existing suite executed | **V** reviewed token used; later conflict retains draft. |
| Network errors not version conflicts | isConcurrencyConflict requires status and code | Editor network/fetch-failure cases | Inspect classifier | **V** ordinary network errors stay errors; failed revision fetch never resolves conflict. |
| Lost access/deletion preserves already-loaded local copyable work | permissionLost/copyableDraft | Editor 403/404/expiry cases | Existing specs executed | **V** loaded editor cases preserve local fields and pending image. |
| Initial-load session expiry reaches recovery UI | Coordinator session handler/loading flag | No existing initial-load case | Added deferred-response audit test | **F** F2 leaves loading true and hides the session message. |
| Overlapping saves/late responses cannot repopulate state | busy, epoch, destroyed guards | Editor, Session, route guard tests | Existing suite executed | **V** duplicate save/destroy/account-switch continuations ignored. Initial loading-state exception F2. |
| Cookbook actions use displayed version and never replay | CookbookEditor membership state | Cookbooks displayed-token conflict test | Existing suite executed | **V** one mutation, refresh only after conflict. |
| Dirty cookbook metadata cannot silently adopt member baseline | Dirty guards; separate baseline/latest | Cookbooks dirty metadata/cover tests | Existing suite executed | **V** member actions blocked; explicit review required. |
| Permission updates alter controls; ownership immutable | CookbookContext and owner controls; server rule | Accounts, Cookbooks, Browser | Existing suite executed | **V** role controls update on refreshed state; owner cannot leave/remove/transfer. No realtime permission push promised. |
| Mobile focus/keyboard/long lists | Native radios, Material focus trap, responsive CSS | Existing Browser | Extended browser 50+50 lists | **V** Tab contained/reaches actions; Space/arrows; focus restored; 320 and390px. Physical devices **N**. |
| Representative previous-schema upgrade preserves data | OptimisticAggregateVersions migration | Upgrade + console legacy migration | Audit-Upgrade realistic quantities/order/member | **V** IDs/old values/images/roles/order preserved, only added version migration. |
| Every existing aggregate receives valid version | Migration random GUID update | Upgrade scans all seeded rows | Audit-Upgrade validates usable tokens | **V** nonempty parseable distinct versions; no FK violations. |
| Migrated entities participate in CAS | Same current operations/mappings | Upgrade originally only read tokens | Audit-Upgrade current+stale writes | **V** recipe and cookbook each 200 then409; winner and children retained. |
| EF snapshot consistency | Migration/snapshot/design factory | EF CLI check | Commands executed without user DB | **V** five migrations, no pending model changes. |
| Deployment design preserves storage boundaries | Runbooks, retained mounts/keys, migration Down refusal | Upgrade gives local compatibility evidence | Configuration/source inspection | **I** documented coordinated rollout and retained paths; no actual deployment. |
| Container/TLS/operational recovery on deployment | Docker/Compose, runbooks | No deployed test environment used | Separate deployment verification | **N** no deployed environment or real data touched. |
| Dependency vulnerability-feed availability | Locked restore | NU1900 output | Network-enabled restore retry | **B** feed unavailable; cached dependency restore/build succeeds. |

## Defects and repair directions

### F1 — P2: conflict review clears local ingredient draft and mandatory review state

Affected requirements: preserve local work/controls during conflict resolution; retain validation/review until explicit action; avoid silently dropping unsaved input.

Locations (frontend):

- `src/app/features/recipes/pages/recipe-editor/recipe-editor-form.ts:260`, particularly clearing `importedIngredients` at 261 and `ingredientDraftPending` at 264.
- `src/app/features/recipes/pages/recipe-editor/recipe-editor-coordinator.ts:429` calls that loading helper for merged local/server values.
- `src/app/features/recipes/components/ingredient-editor/ingredient-editor.component.ts:66` owns the still-visible Quick add input independently.
- Retained reproductions: `shared-editing.audit.spec.ts:63` and `:97` in the recipe-editor directory; failed assertions at 93 and 136–138.

Reproduction A: open a valid saved recipe; another client saves; submit to enter conflict. Enter `200 g cheese` in Quick add without adding it. Review changes, keep local Ingredients, and apply. Expected: input stays pending and Save remains blocked until added/cleared. Actual: input remains visible but its pending flag is cleared; `canSave()` is true. The next save serializes only form rows, so code inspection shows that this still-visible ingredient text is omitted on navigation.

Reproduction B: in the same conflict state, Quick add `1 cup milk`. Before review it produces `236.588 ml` with `requiresReview=true`, and Save is blocked. Keep local Ingredients during review. Expected: required review metadata follows the retained local section. Actual: numeric row remains, its review metadata becomes undefined, save-block reason becomes empty, and Save becomes enabled without confirmation. All three post-review assertions fail in the retained test.

Suggested repair direction: distinguish adopting server data from retaining local ingredient state. Preserve/reassociate local review metadata and pending Quick add state when keeping local content; clear the flag only together with its real input on an explicit discard. Keep the existing required-review check. No repair implemented.

### F2 — P2: session expiry during initial recipe load never releases loading

Affected requirements: recoverable session/access failure; reachable session-ended message and local editor UI; late-response handling.

Locations (frontend): `recipe-editor-coordinator.ts:68` session-change handler, `:151` catch and `:155` finalizer; `recipe-editor.component.html:37` loading branch. Retained reproduction: `shared-editing.audit.spec.ts:49`, failure at `:59`.

Reproduction: authenticate, delay the initial recipe GET/units response, then expire the session before it completes (a 401 triggers this through the interceptor). Complete/reject the old request. Expected: old private response ignored, loading ends, session-ended/load error is reachable. Actual: epoch guard correctly ignores private data but also skips the old finalizer; the session callback never resets loading. `loading()` remains true indefinitely and the template hides the recovery message behind a spinner.

Suggested repair direction: reset the transient loading flag in the session-change handler while preserving epoch checks and the documented local-work policy. Cookbook settings already releases loading on session changes. No repair implemented.

### F3 — P3: matching contracts fail the documented fingerprint check on Windows

Affected requirements: reproducible coordinated contract verification; cross-platform documented toolchain.

Location (backend): `scripts/sync_frontend_contracts.py:48`, `str(target.relative_to(frontend))` as manifest key. Retained reproduction: `tests/shared_editing_contract_audit.py:15`.

Reproduction: run `PY scripts/sync_frontend_contracts.py 'D:\Programmier Umgebung\RecipeAtlasFrontend' --check` on these unchanged repositories. Expected: matching OpenAPI/fixtures/unit definitions and forward-slash manifest pass. Actual: exit2, “Quantity fingerprints need synchronisation”; the separate semantic checker and frontend 191-check runner pass. Generated keys contain Windows backslashes, while tracked manifest keys contain `/`. The new regression exits1 with both key forms in the assertion evidence.

Suggested repair direction: canonicalize manifest keys to POSIX-style relative paths, and test against an existing forward-slash manifest on Windows. Do not regenerate a platform-specific manifest or change the authoritative quantities. No repair implemented.

### F4 — P3: existing browser runner cannot launch Angular on Windows

Affected requirements: discoverable/reproducible browser verification on the installed environment.

Location (frontend): `tests/browser-accounts-smoke.mjs:111`, spawning extensionless `node_modules/.bin/ng` directly. Node emits an unhandled `error` event.

Reproduction: run the documented browser command with this backend and installed Chrome. Expected: start Angular and execute assertions. Actual: exit1, `spawn ...\.bin\ng ENOENT` before any browser assertion. The script had already bootstrapped/started only its temporary backend. Follow-up process inspection found no remaining audit API; the temporary directory was removed.

Suggested repair direction: invoke Angular's JavaScript entry point using `process.execPath` and handle failed child launches so cleanup runs. The new audit launcher demonstrates this approach without modifying the original script or dropping its assertions. Both the 78-check and extended 111-check runs then pass. No production/existing-runner repair implemented.

## Browser evidence and limits

The existing smoke uses native Node/CDP and actual Chrome, not a mocked DOM. It exercised two isolated authenticated browser contexts, actual navigation, stale recipe save, preserved local title, review cancel/apply, whole server step selection, explicit later save, keyboard radio choice, focus entry/return, permission controls, consent/account lifecycle and responsive layouts. The extended variant retains all original assertions and adds longer synthetic lists and smaller viewport height. The audit launcher asserts its source adaptations are found exactly, so it fails if the original runner changes unexpectedly.

Two synthetic screenshots retained and visually inspected:

- [320px conflict comparison](screenshots/mobile-conflict-review.png): stacked local/server choices, scrollable comparison and visible actions. This file was refreshed by the long-list run.
- [390 × 844 long comparison](screenshots/mobile-long-conflict-review-390.png): scrolled to lower sections, long local steps, photo comparison, keyboard-focused Cancel and visible Apply action. Apply is correctly disabled until all choices are supplied.

Other generated screenshots were removed. No claim of physical-device, cross-browser or full accessibility certification is made. The two state-loss findings were reproduced in Angular component tests, not manually clicked through in this browser run.

Remaining manual/browser steps, using a freshly disposable instance and isolated contexts:

1. Reproduce F1 through real clicks, including unfinished Quick add and a cup review badge; fix acceptance requires the visible pending state and Save gate to agree after choosing local Ingredients.
2. Delay the initial recipe request in browser network controls, invalidate that session, and observe F2. Keep the page open long enough to see whether the spinner exits; do not expose response cookies.
3. Select a pending photo, allow text PUT to succeed, pause image PUT, let the second editor save, then release the image PUT. Confirm partial-success text, retained preview, reviewed version and photo-only retry in a real browser. Component tests inject this failure; the complete browser/HTTP sequencing was not injected here.
4. After reviewing a conflict, make another server change before Save. Check another recoverable review using both ingredient and step reorder, then test Escape/Tab/Space/arrows on a physical small-screen browser. Automated component/HTTP tests cover the second conflict; device behavior remains untested.

## Migration evidence

Concurrency migration: `backend/RecipeAtlas.Api/Infrastructure/Persistence/Migrations/20261018164322_OptimisticAggregateVersions.cs:13`. It adds Recipe/Cookbook Version columns, assigns an individual random GUID to old rows, and refuses unsafe Down. Historical migrations were not rewritten.

The previous application was rebuilt from pinned local Git commit `0ce197661b0760d1300cce3efcff097715f2e492` using the existing publish script. This is the recorded completed pre-shared-editing baseline, with schema through `20261018100818_CookbookInvitationsAndProfiles`. It was started against a new temporary database, populated through its actual API, stopped, then replaced only within that test by the audited current binary. This is an upgrade test with existing content, not merely migration of an empty database.

The existing upgrade compared every old column for seeded tables, including identities/memberships/images, before versus after; checked existing cookie usability/key hashes, roles, image/cover bytes, nonempty distinct versions, FK integrity and exactly one added migration. The new upgrade adds Editor membership, reordered steps/ingredients and quantities `''`, `1/2`, `2-3`, `0.125`; compares representations and bytes **before mutation**, then tests successful/current and stale recipe/cookbook saves against migrated rows. Its stale recipe body also attempts replacement children, which remain unchanged after rejection. Recipe save leaves cookbook version unchanged. EF model/snapshot consistency passes separately.

The temporary historical publish has been removed. Reproduction requires first rerunning `publish_pre_concurrency.py` into a new empty audit directory, then passing its DLL path to both upgrade scripts. Do not substitute an existing development database.

## Coverage gaps and inspection-only observations

- No forced write between individual statements of a split GET was scheduled. `ReadSnapshot` and transaction-scoped write-response construction support coherent content/version by inspection; normal HTTP response comparisons pass.
- No database-busy timeout was deliberately induced. Barrier results had the expected 200/409 or 204/400 outcomes; stale aggregate writes carried `concurrency_conflict`, and the rejected invitation carried `invalid_invitation`. The implementation catches only `DbUpdateConcurrencyException`; provider lock/integrity errors are not converted to version conflicts. The tests do not certify extreme contention behavior.
- A narrow post-rollback target deletion edge remains inspection-only: member-change and pending-invitation cancellation callbacks at `CookbookOperations.cs:118` and `CookbookInvitations.cs:88` recheck cookbook access but not the target row. If that row is removed between rollback and recheck, the code can still classify it as an ordinary conflict. `RespondAsync:70` explicitly rechecks its invitation. No unauthorized write follows; a deterministic target-removal interceptor test is needed before classifying this as a proven contract defect.
- Component tests verify separate text/photo calls, response-token chaining, pending blob preservation, network/412 partial success, lost access and no blind replay. A real browser fault-injected photo sequence was not run.
- Cached-image authorization was tested at the HTTP layer with conditional headers and inspected no-store behavior; browser service-worker/offline cache behavior is outside this app's implemented scope and was not tested.
- Historic single-owner/recovery and pre-UI binary compatibility suites were not separately run because the focus was the actual pre-concurrency upgrade. No hosted CI result, live AI/social provider, public invitation delivery, production restore drill, container build, TLS proxy, multi-instance load, physical device or cross-browser result is claimed.
- NuGet vulnerability-feed access was unavailable despite a network-enabled retry. Build success does not establish dependency vulnerability status.

## Audit files and final state

Added in backend:

- `docs/verification/shared-editing-audit.md`
- `docs/verification/screenshots/mobile-conflict-review.png`
- `docs/verification/screenshots/mobile-long-conflict-review-390.png`
- `tests/shared_editing_audit.py`
- `tests/shared_editing_browser_audit.mjs`
- `tests/shared_editing_contract_audit.py` — intentionally failing on Windows until F3 is repaired.
- `tests/shared_editing_upgrade_audit.py`

Added in frontend:

- `src/app/features/recipes/pages/recipe-editor/shared-editing.audit.spec.ts` — three intentionally failing tests for F1/F2.

No existing tracked files were edited. Build/test outputs in existing ignored `bin`, `obj`, `.angular`, `dist` and Python cache directories may have been refreshed; they are not application source changes. No commits/pushes.

Final read-only Git check: both branches and HEADs still equal the audited values above. `git diff --exit-code` returned 0 in both repositories; existing tracked source, assertions, lockfiles and deployment files are unchanged. Status:

```text
Backend (D:\Programmier Umgebung\RecipeAtlas)
?? docs/verification/
?? recipoke-data-backup-20261018-193101/
?? tests/shared_editing_audit.py
?? tests/shared_editing_browser_audit.mjs
?? tests/shared_editing_contract_audit.py
?? tests/shared_editing_upgrade_audit.py

Frontend (D:\Programmier Umgebung\RecipeAtlasFrontend)
?? src/app/features/recipes/pages/recipe-editor/shared-editing.audit.spec.ts
```

The pre-existing backup is the same untracked directory present at the start. The report, screenshots and new tests remain uncommitted and unstaged.

Final resource check found **zero audit API, Angular launcher or browser processes**, and no newly-created `recipoke-*` directories remaining in the test temp root. The historical publish and failed-run browser directory were removed. A failed sandbox run's UI-support directory initially resisted deletion under the other Windows identity; deletion succeeded using its owning sandbox identity, and a final existence check was false. No unrelated process was stopped. Normal ignored build/cache outputs remain available, along with the explicitly retained report/tests/two screenshots.

**Verdict:** repair F1 and F2 before proceeding on the assumption that shared editing is accepted. F3/F4 obstruct the documented Windows verification workflow and should be repaired separately. Backend concurrency/authorization and representative upgrade evidence are strong within the tested scenarios; they do not eliminate the listed coverage gaps.
