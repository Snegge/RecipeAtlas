# Installable Web App verification — 2026-10-29

Both repositories were clean and already on feature/Installable-WebApp. Backend path: D:/Programmier Umgebung/RecipeAtlas, audited HEAD 15dd02b7d46ae9c457caca688fda678738ec97da. Frontend path: D:/Programmier Umgebung/RecipeAtlasFrontend, audited HEAD 98603a0e2ec418c778a954c5879dad715cf77952. Origin fetch succeeded with authorized process permissions; no merge/rebase/stash occurred. No applicable AGENTS.md was found in either checkout or their parent directories.

Executed successfully:

- Frontend `npm test`: 257 tests / 22 files. The existing large application-route test initially exceeded 5 seconds under concurrent load; it passed alone, and its timeout is now 15 seconds. Its new assertion verifies that the update reload action preserves a dirty editor.
- Frontend `npm run build`: production Angular worker generated, initial bundle 771.87 kB (under existing budget).
- Frontend `npm run check:contracts`: 266 transport checks passed.
- Backend `dotnet build --no-restore`: no warnings/errors.
- Backend `dotnet run --no-build --project tests/Recipoke.Tests`: 703 checks, including architecture, SQLite, operations and imports.
- `python tests/accounts_integration.py`: 159 HTTP checks plus concurrent redemption, uniqueness and token-storage assertions.
- `python tests/concurrency_integration.py`: 111 HTTP checks for aggregate/photo atomicity, authorization and reviewed retries.
- Frontend `node tests/browser-accounts-smoke.mjs BACKEND CHROMIUM`: 78 checks at 320/390/899/900/1280px, including accounts, consent, profile, creation, deletion and import entry.
- Backend `node tests/pwa_browser.mjs FRONTEND CHROMIUM`: production build served with real Angular worker and disposable SQLite backend. Desktop and 390px context; manifest parsed; 192/512/maskable/Apple dimensions; Chromium installability diagnostics empty; synthetic beforeinstallprompt invokes browser-prompt handler and appinstalled hides action. Authenticated GET remains outside caches. Offline shell navigation passes with both page network emulation and server sockets disconnected (CDP alone does not reliably affect worker fetches). API/photo/hub requests fail without HTML; offline POST is unsuccessful and is not replayed after reconnect. Cache keys contain no API routes/querystrings. A/B index/hash-manifest update notifies without auto-reload; explicit reload selects B marker.

The A/B fixture uses a changed index and matching manifest, with unchanged application bundles. It tests real worker version installation and coherent index selection; it does not claim two independently compiled application releases or production release infrastructure. Browser installation events are simulated; no operating-system installation or real Android/iOS device was tested.

The existing shopping browser script had stale selectors for a settings toggle and inline Undo from before the current tabs/snackbar UI. Its selectors and enabled-control wait were updated without product changes. Final rerun passed: 14 real Chromium shopping checks with two isolated 390?844 contexts, including membership, realtime updates, editing protection, checkbox keyboard focus, removal/Undo and deletion.

Deployment limitations: Docker CLI exists but no daemon is available, and Caddy is not installed. Therefore Caddy syntax/runtime headers, full containers, public HTTPS, real reverse proxy and asset-retention release/rollback were not executed here. The frontend runbook docs/pwa.md supplies required deployment checks and warns that replacement images alone do not retain old chunks. No live import providers or real user database were used. CI-hosted runs were not observed.

Backend runtime code needs no PWA change: it serves no static SPA, and existing BrowserWriteProtection applies no-store and security headers to every response. ADR 016 and the frontend Caddy policies express that boundary.

Frontend implementation commit: 6797608 (full hash available through git rev-parse). Both final branches and push status are reported in the task completion message.
