# Shared shopping lists: approved design

Approved in the design conversation; implementation explicitly authorized on 2026-10-21 on `feature/shared-shoppinglist` in both existing repositories. No commit, push, worktree, live database use, or unrelated dependency update.

## Scope

Independent named shopping lists, owner/member access unrelated to cookbook membership, accepted invitations to existing active accounts, owner transfer, member leave/removal, and owner-only rename/deletion. Global administrators receive no automatic content access. Owner transfer makes the former owner a member; they may leave separately.

Mobile overview cards preview four unpurchased items and remaining count. Full list has text-only items, fast inline addition, text autosave on Enter/blur, purchased/unpurchased sections, individual removal and five-second Undo. No recipe import, quantity/unit fields, merging, reordering, bulk clearing, service worker, offline queue, or reload recovery. Use current Recipoke styling and accessible keyboard/focus conventions.

## Consistency and security

List administration has a version separate from each item's version. Short SQLite Serializable write transactions begin before current account and list authorization, matching ADR 011. Domain stays framework-independent; application operations may use EF directly per ADR 003; endpoints remain transport-only. Versions participate in atomic writes. Reads return one coherent snapshot.

An item editing reservation is a separate record with user, per-edit-session token, and server expiry. Acquire requires the reviewed item version. Protection lasts 15 seconds after server acceptance, without renewal. Expired tokens cannot save, reacquire, or release a newer reservation. Continuing requires an explicit new reservation against a reviewed current item, with a new token. All checkbox/removal/text operations honor active reservations. Text save and reservation release commit together. Different items remain independent. Same desired checkbox value may succeed as a no-op; changing it requires the current version. Whole-list deletion explicitly overrides reservations after confirmation. No late request recreates a deleted list.

Deleted items are hidden immediately and retained only as Undo recovery data. The deleting actor may restore the same ID/text/purchased state within five seconds of the original deletion commit. Retry does not extend the deadline. Undo identifies the original deletion operation; an older Undo cannot undo a newer deletion. Expired recovery content is purged opportunistically. Receipt history survives for the list lifetime, independently of recovery content.

Every business mutation has a nonempty operation UUID scoped by actor and list. Effect and minimal successful receipt commit atomically. Same operation/type/payload returns its historical result; another payload under that ID fails. Replays check current authorization before returning anything. Successful receipts live until list deletion. Failed, forbidden, rolled-back actions create no successful receipts. Lease traffic uses token semantics instead of receipt history. IDs for new lists/items are client-generated UUIDs, never silently reused after deletion. A deleted list returns unavailable, including on retry. Receipts do not confer access or represent current state.

## Frontend and delivery

HTTP service owns transport. Feature/page-local state owns loaded data, drafts, pending immutable requests and reservations. Use optimistic local checkbox/removal feedback, preserve text and the original operation on uncertain results, and allow explicit retry. Known conflicts retain drafts and require a new reviewed attempt. Expiry/lost access preserves copyable unsaved text; explicit logout/account switch clears private state. Background responses must not replace drafts, pending actions or newer state. Warn before leaving unresolved work; no promise of recovery after reload.

SignalR sends invalidations after commit, not authoritative item patches. Authorized GET reloads the list. Reconnect/open/focus and a 30-second visible-page recovery timer reload current state. Current activity/security stamp/membership must be checked for subscriptions and delivery; removed/deactivated users cannot retain private activity subscriptions. Ordinary operations still use HTTP current-session validation. Notification failure after commit never converts success into a failed business operation. No outbox/broker; recovery refresh covers missed messages.

## Fixed transport contract

All UUIDs are opaque strings; dates are ISO UTC strings. All JSON properties are present, including nullable properties. Browser writes retain `X-RecipeAtlas: 1`. Business DELETE requests also carry `X-Operation-Id`; applicable DELETE preconditions are strong quoted `If-Match`. Other versioned business writes use `expectedVersion`. Reservation DELETE is a token command, not a versioned content deletion.

Responses:

- `ShoppingItemResponse`: `{ id, text, purchased, version, position, editingBy: string|null, reservationExpiresAtUtc: string|null }` (editingBy is display name; never the secret lease token).
- `ShoppingListSummary`: `{ id, name, role, version, preview: string[], activeCount, purchasedCount }`.
- `ShoppingMemberResponse`: `{ userId, username, displayName, role }`.
- `ShoppingPendingInvitation`: `{ id, userId, username, displayName }`.
- `ShoppingListResponse`: `{ id, name, role, version, items: ShoppingItemResponse[], members: ShoppingMemberResponse[], invitations: ShoppingPendingInvitation[] }`. Only owners receive pending invitations; other authorized members receive an empty array.
- `ShoppingInvitationResponse`: `{ id, listId, listName, ownerDisplayName, listVersion }`.
- `ShoppingMutationResponse`: `{ listId, version: string|null, item: ShoppingItemResponse|null, undoUntilUtc: string|null }`. Version is the administrative version for administrative mutations; item carries its own version. Leave/removal of self/decline return no protected data (nullable fields null).
- `ShoppingReservationResponse`: `{ token, expiresAtUtc }`.

Routes under `/api/shopping-lists`:

| Method/path | Input | Output |
|---|---|---|
| GET `/` | none | summary array |
| POST `/` | `{ listId, name, operationId }` | mutation |
| GET `/{listId}` | none | list |
| PUT `/{listId}` | `{ name, expectedVersion, operationId }` | mutation |
| DELETE `/{listId}` | If-Match, X-Operation-Id | 204 |
| POST `/{listId}/invitations` | `{ username, expectedVersion, operationId }` | mutation |
| DELETE `/{listId}/invitations/{id}` | If-Match, X-Operation-Id | mutation |
| POST `/{listId}/invitations/{id}/respond` | `{ accept, expectedVersion, operationId }` | mutation |
| DELETE `/{listId}/members/{userId}` | If-Match, X-Operation-Id | mutation |
| POST `/{listId}/leave` | `{ expectedVersion, operationId }` | mutation |
| POST `/{listId}/transfer` | `{ newOwnerUserId, expectedVersion, operationId }` | mutation |
| POST `/{listId}/items` | `{ id, text, operationId }` | mutation |
| PUT `/{listId}/items/{id}` | `{ text, expectedVersion, token, operationId }` | mutation |
| PUT `/{listId}/items/{id}/purchased` | `{ purchased, expectedVersion, operationId }` | mutation |
| DELETE `/{listId}/items/{id}` | If-Match, X-Operation-Id | mutation with Undo deadline |
| POST `/{listId}/items/{id}/restore` | `{ deletionOperationId, operationId }` | mutation |
| POST `/{listId}/items/{id}/reservation` | `{ token, expectedVersion }` | reservation |
| DELETE `/{listId}/items/{id}/reservation` | JSON `{ token }` | 204 |

GET `/api/shopping-list-invitations` returns invitation array. SignalR `/api/shopping-events`, `Watch(listId)` / `Unwatch(listId)`, client events `Changed(listId)` and `AccessLost(listId)`; no item text or usernames in event payloads. Connection setup uses established same-origin cookie/header protection; no weakening general write protection.

Limits: list name 1–100 trimmed characters; item text 1–500 trimmed characters; at most 1000 live/recoverable items per list; no unbounded list-content receipt serialization. Invitation username follows existing account lookup. Error codes distinguish `shopping_item_reserved`, `shopping_reservation_lost`, `shopping_undo_expired`, `operation_id_reused`, and existing concurrency/access errors.

## Verification

Implementation clarification: retired list UUIDs remain as ID-only markers so a delayed create cannot recreate a deleted list after its receipts have gone. Removed item rows keep only consumed identity/version/order after their recovery payload expires. This is identity protection, not an offline change log. Receipts may retain one historical item result (at most 500 text characters), never an entire list.

SignalR uses its LongPolling transport on both ends. It is immediate server notification delivery, independent of the 30-second recovery refresh. This preserves the existing custom-header protection for hub writes without adding a browser WebSocket origin exception. The frontend SignalR client is the only new dependency.

Real SQLite independent contexts: authorization, atomic rollback/receipts, races for add/reservation/edit/delete, fake-clock 15-second expiry/5-second Undo, post-restart retries, no cross-list ID access, member removal/deactivation, ownership, schema upgrade preserving recipes. Frontend tests: autosave, pending immutable requests, same-ID retry, no replay of stale receipt as current state, late GET protection, draft retention, expiry, revoked access and mobile focus. Isolated HTTP/browser scenarios cover two users, live invalidation, 390×844 viewport and keyboard actions. No live imports/AI or existing databases.
