# Shared Shopping Lists Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development or superpowers:executing-plans, with focused tests and independent review. User explicitly authorized implementation after approving the chat design; stay on the existing feature branches and leave all work uncommitted.

**Goal:** Complete the approved online household shopping workflow across both repositories.

**Architecture:** Existing feature-oriented ASP.NET Core monolith, framework-independent domain, direct EF application operations, SQLite, thin HTTP endpoints. Angular feature-local state and HTTP service; SignalR invalidation with authorized refresh.

**Tech Stack:** .NET/EF Core 10, SQLite, Angular 22, Node 24, existing custom console/HTTP/Vitest/browser runners.

**Spec:** `docs/superpowers/specs/2026-10-21-shared-shoppinglist-design.md`

## Global constraints

- Existing clean branches: `feature/shared-shoppinglist`; backend baseline `5e798cfd4ffea616d36cca884e9e3b9fac6c6130`, frontend `dcac417973ee6fee6f38b40d7a5c5be997c7580b`.
- No commits/pushes, no unrelated refactor/upgrades, no real user data. Every backend process gets disposable absolute DataDirectory before startup migrations.
- Reservation 15 seconds without renewal, Undo 5 seconds, visible recovery refresh 30 seconds.
- Interface contract is fixed in the spec. Changes require coordination, not divergent implementations.

## Review focus

- Lost save response after lease release returns the receipt, without another write.
- Old deletion retry after Undo cannot remove the restored item; old Undo cannot restore a newer deletion.
- Removed/deactivated users cannot read receipts or continue receiving list activity.
- Delayed frontend responses never replace active edits, pending actions or newer snapshots.
- Expired/new editing sessions use distinct tokens; old release cannot delete a newer reservation.

## Task 1: persistence and application operations

Files: new `backend/Recipoke.Domain/Shopping/`, `Features/Shopping/Application/`, persistence configuration and additive migration; modify `RecipeDb.cs`; new `tests/Recipoke.Tests/ShoppingTests.cs` and runner registration.

- [x] Write behavioral SQLite tests using controlled clock and separate contexts; observe failure before implementing.
- [x] Implement domain models/rules, centralized ShoppingAccess, item/list/invitation/reservation operations and atomic receipts per spec.
- [x] Generate additive EF migration and verify upgrade/pending-model consistency.
- [x] Run custom console suite; review authorization ordering, atomicity, retry and token lifecycles.

## Task 2: HTTP and delivery

Files: `Features/Shopping/Api/ShoppingEndpoints.cs`, `Features/Shopping/Infrastructure/ShoppingHub.cs` and notification adapter, feature registration, `Program.cs`, API description; `tests/shopping_integration.py`.

- [x] Add failing isolated HTTP checks for the fixed routes and status/error contracts.
- [x] Implement thin endpoints and SignalR invalidations; subscriptions/delivery recheck membership/activity/security stamp.
- [x] Add HTTP tests for simultaneous requests, receipt replay after restart, response loss semantics and account removal.
- [x] Export OpenAPI and synchronize frontend snapshot; extend checker with actual shopping types.

## Task 3: frontend manual shopping workflow

Files: `core/models/shopping.model.ts`, `core/services/shopping.service.ts`, new lazy `features/shopping/`, navigation entries, focused specs, SignalR package and proxy config only if needed.

- [x] Write failing service/component tests against the fixed contract.
- [x] Implement overview cards, invitations, owner/member actions, detail checklist, quick add, inline autosave, reservation expiry, checkbox/remove/Undo pending states.
- [x] Preserve immutable uncertain requests and drafts; fresh reviewed actions get fresh operation IDs. Handle session epochs and late responses.
- [x] Implement notifications/open/focus/reconnect/30-second refresh; fixed 15-second reservations require explicit reacquisition after expiry.
- [x] Run frontend full test suite and production build, fixing failures without weakening assertions.

## Task 4: integration review and completion evidence

Files: browser shopping smoke test, ADR 015, runbook and verification report; CI runner additions.

- [x] Exercise two browser contexts with disposable database and 390×844 viewport, notification delivery, reservations, Undo, owner deletion and account revocation.
- [x] Review complete changes against spec, fix defects with regression tests.
- [x] Run final relevant backend build/custom console/HTTP, frontend test/build/contracts, EF pending model and cross-repository contract checks.
- [x] Record exact evidence, unresolved limitations, final git status; clean only own processes/temp data.

## Progress and implementation decisions

- Planning complete; both repositories already on requested clean feature branch.
- User's explicit implementation request supersedes earlier discussion-only restriction. No automatic commit/worktree workflow is used.
- Ruling: use same-actor Undo and stable insertion order as approved design defaults; no drag reorder or bulk clearing in this MVP.
- Ruling: retain consumed list IDs without content after deletion; otherwise a delayed creation retry could recreate a deleted list. Cost: one UUID marker per deleted list.
- Ruling: SignalR LongPolling preserves the existing write header on browser requests. Cost: held HTTP requests rather than WebSocket connections; notifications remain server-driven.
- RED evidence: isolated `tests/shopping_integration.py` fails with expected 404 for shopping creation, then for SignalR negotiation before implementations. Sandbox bootstrap failed first; elevated disposable rerun reached the behavioral assertion.

## Completion evidence

Completed on the existing feature branches, uncommitted. Backend: 703 console checks, 73 shopping HTTP checks, 8 live notification checks plus 29 fixture HTTP checks; existing suites and populated upgrades passed. Frontend: 240 tests, production build, 266 contract checks. Chromium: 14 checks with two contexts at 390 by 844. Final review found no remaining blocker. See `docs/verification/shared-shoppinglist.md` for commands, migration evidence, changed files and limitations. Temporary test processes/data were removed.
