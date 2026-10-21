# Shared-editing audit fixes

Spec: findings F1–F4 in [the audit](shared-editing-audit.md). Implement on the existing branches; no commits, dependency changes, database changes or unrelated cleanup.

## Plan

- [x] F1: retain local ingredient controls, review metadata and pending Quick add state when review keeps Ingredients. Clear the actual Quick add input only after an accepted server-section replacement/discard. Cover local retention, server replacement and cancellation with component tests.
- [x] F2: release initial loading in the session-change handler; keep epoch checks so stale private responses stay ignored. Use the retained deferred-response test.
- [x] F3: use POSIX relative manifest keys in the existing sync script. Keep hashes/contracts unchanged. Run the retained failing checker and existing path/read-only tests.
- [x] F4: invoke Angular's JavaScript CLI with Node; await child startup errors and skip unspawned children during cleanup. Preserve browser assertions and update the audit launcher to use the repaired runner. Test a failed child launch and execute the original browser smoke with isolated data.
- [x] Run focused tests, frontend suite/build/contracts, independent review and final status checks. Preserve the original audit as historical evidence and record repair results here.

## Scope and review focus

Frontend files: recipe editor form, coordinator, component, retained audit spec, and browser runner. Backend files: sync script, path tests, audit browser adapter and repair notes. Existing APIs, domain/persistence operations and lockfiles need no changes.

Check that local review metadata stays attached to its actual FormGroup; server section choices do not keep stale review flags; canceled/expired dialogs do not erase pending input; session changes never apply late private responses; failed process launch cleans up only audit resources. The two editor regressions and Windows fingerprint regression were rerun and failed before implementation.

## Implemented changes

- `RecipeEditorForm.applyRecipe` can retain the actual local ingredient controls and their associated review map/draft flags. The coordinator uses that path only when the user keeps local Ingredients. Successful server section replacement returns a signal so the component clears its child Quick add input together with the form flag; cancel/expiry does neither.
- The session callback releases loading before preserving expired-session work or clearing an explicit account change. Existing epoch checks still ignore late private responses.
- The sync script uses `Path.as_posix()` for manifest keys; hashes and authoritative contract files are unchanged. A portable-key regression was also added to the existing CI path-test runner.
- The browser runner uses Node's executable with Angular's installed JavaScript entry point, awaits spawn/error, and skips unspawned children during cleanup. The audit adapter no longer replaces Angular's launch command. A separate missing-browser regression exercises the real runner's failure/cleanup path.

## Executed verification (2026-10-21)

All commands below use the unchanged existing branches and dependency versions. Backend root is `D:\Programmier Umgebung\RecipeAtlas`; frontend root is the sibling `D:\Programmier Umgebung\RecipeAtlasFrontend`. `PY` is the installed `C:\Users\Marco\AppData\Local\Python\pythoncore-3.14-64\python.exe` interpreter.

| Directory | Command | Final result |
|---|---|---|
| Frontend | `npm.cmd test -- --include=src/app/features/recipes/pages/recipe-editor/shared-editing.audit.spec.ts` | Exit 0; **9/9** pass. Covers original three failures, retained cup FormGroup identity, explicit server discard, and cancel/expired dialog results for both review and server loading. |
| Frontend | `npm.cmd test` | Exit 0; **211/211**, **17 files** pass. |
| Frontend | `npm.cmd run build` | Exit 0; **757.00 kB** initial bundle, unchanged budgets. |
| Frontend | `npm.cmd run check:contracts` | Exit 0; **191 checks** pass. |
| Backend | `PY tests/test_contract_paths.py` | Exit 0; **5/5** pass, including portable manifest keys and existing read-only/drift assertions. |
| Backend | `PY tests/shared_editing_contract_audit.py 'D:\Programmier Umgebung\RecipeAtlasFrontend'` | Exit 0; **1/1** pass against the actual matching repositories. |
| Backend | `PY tests/check_shared_contracts.py 'D:\Programmier Umgebung\RecipeAtlasFrontend'` | Exit 0; semantic equality. |
| Backend | `PY scripts/sync_frontend_contracts.py 'D:\Programmier Umgebung\RecipeAtlasFrontend' --check` | Exit 0; fingerprint/contract equality without writes. |
| Frontend | `node tests/browser-accounts-smoke.mjs 'D:\Programmier Umgebung\RecipeAtlas' 'C:\Program Files\Google\Chrome\Application\chrome.exe'` | Exit 0; **78 browser checks** directly through the repaired original runner. |
| Backend | `RECIPOKE_AUDIT_MOBILE=1` then `node tests/shared_editing_browser_audit.mjs 'D:\Programmier Umgebung\RecipeAtlasFrontend' 'D:\Programmier Umgebung\RecipeAtlas' 'C:\Program Files\Google\Chrome\Application\chrome.exe'` | Exit 0; **111 checks**, including 390 × 844 and 50-item ingredient/step lists. Screenshots disabled to preserve historical audit images. This overlaps the 78-check run. |
| Backend | `node tests/shared_editing_browser_startup_test.mjs 'D:\Programmier Umgebung\RecipeAtlasFrontend' 'D:\Programmier Umgebung\RecipeAtlas'` | Exit 0; deliberate missing browser fails normally and its temporary database/profile/log directory is removed. |

Before the fixes, the retained three editor regressions and real-checkout fingerprint regression failed again. Two added server-discard tests also initially failed, proving visible Quick add state needed coordinated clearing. Their fixture now renders the input event before the simulated dialog, matching actual Angular input binding. The startup regression's first cleanup assertion found Node's separate compile cache; it now checks the runner-owned directory prefix, and removes the enclosing test directory/cache afterward.

Independent review found no concrete defect in the fixes. Its request for cancel/expiry Quick add coverage was addressed by the four additional dialog cases. No existing assertion was removed or weakened. The old audit report and screenshots remain historical evidence; its intentionally failing tests now pass after the source changes.

Backend application, authorization, migrations, dependencies and lockfiles were not changed. Their audit evidence is retained; broad backend HTTP/migration suites were not rerun for these frontend/script fixes. Production deployment, physical devices, live providers and NuGet vulnerability-feed availability were not rechecked.

## Final state

All four confirmed findings are repaired and their relevant checks pass. No confirmed blocking defect remains from F1–F4. The original audit's unrelated coverage gaps and inspection-only observations are not promoted to verified behavior by these repairs.

Final `git diff --check` passes in both repositories. Both remain on `feature/shared-editing`, at backend `1edf4ef4e00224516d3e52a8bae1e838db86f69f` and frontend `1b5db9b9e74ebe9fa516b4dc3da5305eb008fb9a`; nothing is committed, staged or pushed. No lockfile diff. Existing backup and prior audit artifacts remain intact.

Modified existing backend files: `scripts/sync_frontend_contracts.py`, `tests/test_contract_paths.py`. Modified existing frontend files: `recipe-editor-form.ts`, `recipe-editor-coordinator.ts`, `recipe-editor.component.ts` in `src/app/features/recipes/pages/recipe-editor/`, and `tests/browser-accounts-smoke.mjs`.

Updated prior uncommitted audit artifacts: `docs/verification/shared-editing-audit.md` gets a historical-state/fix link; `tests/shared_editing_browser_audit.mjs` drops the obsolete launch workaround; frontend `shared-editing.audit.spec.ts` grows from three failing tests to nine passing tests. Added this repair note and `tests/shared_editing_browser_startup_test.mjs`. Other audit scripts/screenshots were preserved.

Final process inspection found zero audit API, Angular or browser processes and no new `recipoke-*` temporary directories from these repair tests. Only normal ignored dependency/build caches remain; no unrelated process was stopped.
