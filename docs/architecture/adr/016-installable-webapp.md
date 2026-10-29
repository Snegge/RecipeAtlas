# ADR 016: Installable public application shell

Accepted, 2026-10-29. Angular 22's maintained service worker owns only public shell caching in the frontend. Prefetch all versioned bundles so lazy modules can render offline. Explicit frontend navigation allowlisting and no data groups keep APIs, authenticated images, invitation responses and SignalR network-only. No browser cache logic enters domain or application operations. No database changes are needed.

Use a full-page reload after user choice, guarded by existing DirtyPage state and active API writes. Never activate a new Angular version inside a running page. Browser online state is a hint; request failures cannot establish logout/session expiry. Existing account-generation cleanup, concurrency, shopping retries and events remain authoritative.

Caddy in the frontend deployment owns static MIME/cache policies; the API already emits no-store through BrowserWriteProtection for all responses, including errors and protected media. The backend-only proxy cannot host a PWA alone. No backend runtime change is necessary. The matching frontend runbook `docs/pwa.md` documents same-origin root hosting, HTTPS, release ordering, retained chunks, rollback, installation and diagnostics. A future shopping synchronization phase requires its own design; private persistence and replay are excluded here.

Alternatives rejected: custom worker/Workbox duplicates Angular lifecycle; caching GET APIs risks membership/session leaks; forced reload loses drafts; clearing all origin storage harms unrelated applications. Existing baseline had no worker/private worker cache requiring migration.
