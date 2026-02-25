# Bugence Edit Console

The visual editor now supports in-context editing on the live site canvas. After you sign in, head to the dashboard or content library and press **Edit** on any page. The button opens a new tab that renders the actual static page (for example `index.html`) with editing affordances layered on top.

## How in-context editing works

- Hover over any text or image to reveal the floating **Edit** badge. Locked sections display a red badge and cannot be modified without concierge access.
- Double-click a block or press the badge to open the rich editor modal. The modal includes formatting controls (bold, headings, lists, links) and mirrors the toolset from the dashboard forms.
- Saving commits updates through the new `/api/content/pages/{pageId}/sections` endpoint. The API persists changes via `PageSection` records and tracks the DOM selector that anchors each element.
- You can edit any piece of copy - even if it was not pre-mapped. The editor computes a unique CSS selector the first time you save, so future sessions reload that content automatically.
- Image blocks open an upload workflow with live preview and alt-text editing. Existing imagery stays in place if you only adjust the description.

## Technical notes

- The canvas is served from `GET /content/canvas/{pageId}` which injects `/editor/bugence-editor.css` and `/editor/bugence-editor.js` into the static HTML file.
- `PageSection` now includes a `CssSelector` column (migration `20251012050600_AddSectionSelector`) that stores the DOM anchor for in-context edits.
- The overlay uses fetch requests with same-origin cookies; no additional tokens are required for authenticated users.

This flow supports the four core pages (`index`, `meet-pete-d`, `book-pete-d`, `join-community`). Add a new static file mapping inside `Program.cs` if more pages join the experience.

## Front-end build pipeline

- Dashboard chrome scripts live in `packages/dashboard/src/site.ts`. Run `pnpm --filter @bugence/dashboard build` to output `wwwroot/js/site.js` (IIFE bundle for Razor layout).
- Canvas overlay scripts live in `packages/canvas/src/bugence-editor.ts`. Run `pnpm --filter @bugence/canvas build` to output `wwwroot/editor/bugence-editor.js`.
- Shared interfaces and utility types are published from `packages/core/src`. Regenerate TypeScript + C# contracts from `BugenceEditConsole/OpenApi/content.openapi.yaml` with `pnpm --filter @bugence/core generate`.

## State & data layer

- `/api/content/pages`, `/api/content/sections`, and `/api/content/history` now emit ETags and accept `If-Match` headers for optimistic workflows.
- `packages/core` exposes `createStore`, fetch helpers, and shared types used by `packages/dashboard/src/store/dashboardStore.ts` and `packages/canvas/src/store/canvasStore.ts`.
- Stores provide optimistic updates and conflict handling; call `loadDashboardPages`, `loadDashboardHistory`, `loadCanvasSections`, or `saveCanvasSection` from modular scripts to hydrate UI state.
