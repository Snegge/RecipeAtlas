# ADR 015: Independent shared shopping lists

## Status and context

Accepted for the online MVP. The design conversation selected lists shared independently of cookbooks, text-only items, manual entry first, server notifications, 15-second editing protection **without renewal**, five-second Undo, and owner transfer. Whole-list deletion requires confirmation and overrides item editing protection. Offline storage and reload recovery are deferred.

ADR 003 (direct EF application operations), 006/009 (cookie sessions), 007 (contracts), 011 (authorization ordering), and 014 (atomic version preconditions) remain applicable. ADR 014's whole-recipe concurrency boundary is not copied to a shopping list: different people must be able to work on different items without unnecessary conflicts.

## Alternatives and decision

**Ownership:** cookbook-scoped lists reuse memberships but couple household shopping to broader recipe-sharing groups. A household entity would add a second management screen before there is another household feature. Use independently named lists with Owner/Member membership and explicit invitations to existing active accounts. Both roles edit items; only the owner renames/deletes the list, invites/removes others, or transfers ownership. An owner must transfer before leaving. Global administration grants no content access.

**Concurrency:** a single list version would conflict on unrelated item changes. Separate versions for text and checked state would permit more simultaneous updates but contradict the chosen protection while editing. Use one administrative list version and one version per item. Item writes do not advance the administrative version. Every content mutation uses atomic EF concurrency predicates inside the application-owned SQLite write transaction. The transaction starts before current account and membership checks. Domain models/rules have no EF, ASP.NET or HTTP dependencies.

Text editing acquires a server reservation tied to actor, item and a fresh edit-session token. It lasts 15 seconds and cannot renew. Acquiring the same still-active token returns the original expiry. Expired/released tokens never become active again. Other text, check and removal operations are blocked during protection, even from another tab of the same account. Text save checks the item version and reservation and releases it atomically. After expiry, local text remains copyable; a new explicit reviewed attempt needs a new token. Whole-list deletion deliberately overrides reservations.

Checkbox requests express a desired boolean. Already-satisfied requests are no-ops; actual changes require the expected item version. Delete-versus-edit is resolved by the reservation and version checks. Deletion hides the item immediately; only the deleting actor may restore that same ID and content within five seconds of the original deletion. A retry cannot extend this window. Bulk clearing and reordering are excluded.

**Retries:** a client-generated operation UUID identifies one logical business action, scoped by actor and list. A database unique key, typed operation payload hash, and minimal successful response receipt commit in the same transaction as the effect. Concurrent identical requests return one recorded result; a changed payload under the same ID returns `409 operation_id_reused`. Failed/rolled-back operations have no successful receipt. Current authorization is checked before replay; a historical receipt is never a current snapshot or an access grant. A deliberately repeated action has a new operation ID and, for additions, a new item ID.

Receipts live for the list's lifetime. They may retain one historical item text, including after item deletion; they are not a complete audit log. Whole-list deletion purges its content and receipts but retains a content-free consumed list UUID. Removed item rows retain only consumed identity/version/order once Undo recovery expires. Consumed reservation tokens prevent delayed acquisition from reviving old protection. These records protect against late requests; they are not an offline change feed. This is one committed effect per retained operation identity, not a promise of exactly-once network delivery.

**Updates:** manual refresh alone is inadequate for two phones shopping together. SignalR sends `Changed(listId)` and `AccessLost(listId)` invalidations after commit; HTTP GET remains authoritative. The adapter rechecks activity, security stamp and current membership before delivery. The in-memory subscription registry never grants access. Notification failure does not roll back or report failure for an already committed write. Open/focus/reconnect and a visible 30-second recovery refresh cover missed notifications, including process restarts.

Use SignalR LongPolling on both ends because browser WebSockets cannot carry the existing custom write header. Held requests still deliver immediate server notifications. This preserves the existing header/cookie security convention without an origin-exception path. No broker, outbox, event sourcing or generic synchronization framework is introduced. One API instance remains the deployment boundary.

## Frontend ownership and contracts

The existing Angular HTTP service layer owns requests. Shopping page-local state owns snapshots, drafts, reservations and immutable uncertain commands. A lost response is an unknown outcome, not evidence the operation failed: retry the same command ID/payload, then reload current state. A conflict requires review and a new action; never blindly replay it with a newer version. Explicit account changes clear private state; permission loss preserves copyable unsaved input but blocks writes. Pending state is not persisted across reloads.

The overview uses small named list previews. The detail page uses text-only inline editing with Enter/blur save and separate purchased items. Checkbox/removal feedback is immediate, with pending/error handling; typed input survives failed saves. Background refresh must not change a dirty draft's reviewed baseline. Owner delete uses the established confirmation dialog.

Version preconditions and status codes follow ADR 014: body `expectedVersion`, strong `If-Match` for business DELETE, `428` missing, `400` malformed, `409` stale body, `412` stale header. Business DELETE adds `X-Operation-Id`. Reservation acquire/release has token semantics and no renewal endpoint. JSON booleans are required explicitly. List/item names are bounded; a list allows at most 1000 live or recoverable items. Detailed routes are in the [design record](../../superpowers/specs/2026-10-21-shared-shoppinglist-design.md).

## Consequences and validation

The additive `SharedShoppingLists` migration leaves existing tables/data intact. Its `Down` rejects destructive downgrade; rollback requires coordinated binaries and a full backup. Migration tests use populated synthetic data, not only an empty schema. Tests also exercise real SQLite independent connections, barriers, injected rollback, expiry, receipts after restart, current authorization, transport contracts and two browser contexts.

Stable item IDs, explicit operations and version/retry rules support later synchronization. Durable client queues, tombstone feeds/cursors, recipe import, serving calculations and PWA behavior remain separate decisions. Revisit receipt retention if actual database growth warrants a bounded retry horizon; revisit notification transport/registry before multiple API instances. See the [runbook](../../shared-shopping-lists.md) and [verification evidence](../../verification/shared-shoppinglist.md).
