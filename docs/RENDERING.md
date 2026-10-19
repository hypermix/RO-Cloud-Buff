# UI rendering

The desktop UI uses plain JavaScript in WebView2. It does not use React.

## Ownership and updates

- incremental-ui.js mounts navigation, main viewport, footer and dialog/notice slots once.
- Navigation replaces only the current page content. The app shell and viewport remain mounted.
- Cards have stable IDs and independent render signatures. Unchanged cards skip template generation and DOM reconciliation.
- Dictionary/discovery rows use state-ID keys and memoized markup. A single dictionary edit regenerates only that row template.
- Reconciliation changes attributes/text in existing nodes and moves keyed nodes. Inputs, expanded details and scroll containers retain identity.
- Live buff updates modify the selected row, group order and summary. Repeated updates for the same card/status in one frame are coalesced.
- Repeated discoveries are coalesced, with one sort pass after the batch.
- Timer ticks write only changed values. Shared counters skip identical text.
- Scroll deferral remains for structural edits, but deferred updates no longer replace the dialog or the page shell.

## Verification

From the repository root, regenerate the UI with `python build-ui.py`, then:

```powershell
cd tests
npm install
npx playwright install chromium
npm test
```

Optional environment variables: RO_CHROMIUM_EXECUTABLE selects a Chromium executable; RO_PLAYWRIGHT_MODULE selects an existing Playwright module.

Tests use mock native events and illustrative data, not live game traffic. The rendering test uses 20 clients and 400 buff rows and checks retained node identity, a 200-event burst coalescing into one row update, isolated card folds, and one-row dictionary edits. Other tests cover registration opt-out, dismissible undo feedback, live discovery sorting and scroll/dialog retention.

`window.roRenderDiagnostics.snapshot()` exposes counters for debugging; `reset()` clears them. Timings are local headless Chromium measurements, not an FPS or native WebView2 performance guarantee.

No new native executable is produced by these UI tests. Profiling during a future real-game run is still needed for CPU/GPU and long-session behavior.

## Character storage

`onBoard` controls visibility only (missing means true for existing profiles). Native bindings and timers continue for stored profiles. Hidden cards do not schedule visual buff paints. Storage entries are keyed by profile ID and update independently of the monitor. Board ordering excludes stored profiles. `tests/storage.cjs` covers connection retention, off-board alert calls, current state on restore, profile creation/search, persisted visibility and narrow layouts.

## Alert evidence

Alerts require a current-session active observation for the selected card/status and a connected card. Successful rebind, reset and unbind clear live timers and observation evidence. Repeated active messages with expiry timestamps within one second preserve warning/expiry deduplication. Confirmed map-connection transitions preserve evidence. Settings shows up to 30 session-only alert records (card, status, cause, stored state). This identifies alert origin; it does not establish that every decoded protocol event is correct. `tests/alerts.cjs` covers unobserved timers, warning deduplication and rebind followed by idle time.

Deselected buff rules (`hidden: true`) never emit warning, expiry or removal sounds even when their saved alert preference is true. This is independent of profile storage: an entire stored profile still alerts for its selected buffs. `tests/unselected-alerts.cjs` covers the distinction and reselecting a buff.
