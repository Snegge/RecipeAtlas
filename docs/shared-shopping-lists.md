# Shared shopping lists

Shopping lists are separate from cookbooks. An accepted Owner or Member can add/edit/check/remove text items. Owners also manage invitations, rename/delete the list and transfer ownership. Membership is never inferred from administrator status or cookbook access. Invitations require existing active accounts and acceptance before content access.

The overview shows named preview cards. The detail page autosaves inline text on Enter/blur, retains checked items in a separate section, and offers five-second Undo after removing an item. Editing protection lasts **15 seconds without renewal**. After expiry, review current content before explicitly obtaining new protection; unsaved text remains available. Whole-list deletion requires owner confirmation and overrides active edits.

HTTP requests own writes; SignalR LongPolling delivers invalidations. The client refreshes authorized content on notification, open/focus/reconnect and every 30 seconds while visible. A connection indicator does not guarantee every notification arrived. Retrying an uncertain write uses its original operation ID and exact payload. It does not repeat a new user action. Reload recovery and offline editing are not implemented.

## Deployment and rollback

1. Stop writes/API processes and take a consistent full SQLite backup plus the existing Data Protection keys and configuration. Preserve WAL consistency; do not copy a live database file alone.
2. Build and verify both matching `feature/shared-shoppinglist` repositories. The only added frontend runtime dependency is `@microsoft/signalr` 10; framework versions remain pinned.
3. Apply `20261021125240_SharedShoppingLists`. Startup migrations remain enabled, so verify the effective absolute `DataDirectory` before starting the API. The new migration only adds shopping tables/indexes; existing accounts, recipes, cookbooks and image storage remain intact.
4. Deploy the matching frontend. Preserve same-origin `/api` proxying, including `/api/shopping-events` negotiation and long-poll requests. Keep the existing custom write header. Avoid a proxy timeout shorter than SignalR's held poll. No WebSocket proxy is required for this implementation.
5. Smoke-test with synthetic accounts. Downward migration is deliberately rejected; rollback restores the complete pre-upgrade backup and matching old binaries/SPA. Handle post-backup writes explicitly before rollback.

The subscription registry is in-process. This release supports one API instance. A restart loses subscriptions but not committed receipts or effects; clients reconnect and refresh. Receipt rows live until list deletion and can retain historical item text. Expired Undo payload is cleared on subsequent mutations, while consumed IDs remain to reject late recreation. This is not a retention/erasure policy for a production service.

## Reproducible checks

From the backend root, with the documented .NET 10.0.401 SDK and Python 3:

```sh
dotnet build --no-restore
dotnet run --no-build --project tests/Recipoke.Tests
python tests/shopping_integration.py
dotnet ef migrations has-pending-model-changes --no-build --project backend/RecipeAtlas.Api
python scripts/export_openapi.py --check
python tests/check_shared_contracts.py ../RecipeAtlasFrontend
python scripts/sync_frontend_contracts.py ../RecipeAtlasFrontend --check
node tests/shopping_events.mjs ../RecipeAtlasFrontend
node tests/shopping_browser.mjs ../RecipeAtlasFrontend /absolute/path/to/chromium
```

Use `python3` where appropriate. The Node checks require the frontend's existing lockfile installation; Chromium is external to npm. Each script starts only disposable local data/processes and cleans its own resources. The console project is a custom runner: `dotnet test` is not a substitute. Event/browser scripts are explicit cross-repository local checks, not claimed as independent backend CI coverage.

From the frontend root:

```sh
npm run check:contracts
npm test
npm run build
```

On PowerShell installations that block `npm.ps1`, use `npm.cmd`. Do not use `npm audit fix --force` to bypass unrelated advisories.

## Two-phone manual verification

1. Use separate browser contexts and synthetic existing accounts. Create a list, invite the other account, accept, and open the list on both phones. Cookbook membership must have no effect.
2. Add distinct items simultaneously. Both should appear. Edit one item; the other phone must show protection and disable check/removal for that item. Editing a different item remains possible. Save by Enter and blur.
3. Type for more than 15 seconds. Protection must expire without renewal. Preserve the text, compare with current server content and explicitly reacquire before saving. An intervening change must not be silently overwritten.
4. Check/uncheck an item and remove/Undo another. Observe both phones. A delayed retry of removal after Undo must not remove the restored item again.
5. Interrupt a request's response after commit. Retry the same pending action. Confirm one effect, then deliberately add the same text again and confirm a distinct item.
6. Remove membership or delete the list while the other phone has unsaved text. It must stop writing, retain copyable text, and avoid late recreation. Confirm the owner delete dialog can be cancelled and operated by keyboard.
7. At 390 by 844, inspect long item text, purchased items, settings and confirmation dialogs. Check reachable actions, focus after checking/removal, and no horizontal scrolling.

Executed evidence and remaining gaps belong in [the verification report](verification/shared-shoppinglist.md); these steps alone are not proof of execution.
