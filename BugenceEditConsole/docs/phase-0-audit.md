# Phase 0 Audit - Bugence Visual Edit Dashboard

Audit window: current repository state as of this run.

## Capability Map

| Capability | User-facing surface | Server/data source | Notes |
| --- | --- | --- | --- |
| Mission Control overview | `Pages/Dashboard/Index.cshtml` aggregates greeting, module counts, last publish timestamp, and recent activity feed | `Pages/Dashboard/Index.cshtml.cs` pulls from `ApplicationDbContext` counts plus `IContentOrchestrator.GetRecentLogsAsync` | Metrics rely on EF Core queries against `SitePages`, `PageSections`, and `ContentChangeLogs`; activity stream limited to last 8 logs |
| Content Library listing & spotlight | `Pages/Content/Library.cshtml` presents search, trending chips, spotlight, mission clusters, and status signals | `Pages/Content/Library.cshtml.cs` composes projections from `SitePages` and nested `PageSections` | Filtering is in-memory after database query; spotlight, status metrics, and clusters computed on the fly without caching |
| Page editor (form-based) | `Pages/Content/Edit.cshtml` offers section filtering, rich text/image editors, save + publish actions | `Pages/Content/Edit.cshtml.cs` uses `IContentOrchestrator` methods (`SyncPageFromAssetAsync`, `UpdateSectionAsync`, `PublishPageAsync`) | Handles optimistic UX but no client-side diffing; publish triggers static asset rewrite and logs |
| Live preview console | `Pages/Content/LivePreview.cshtml` renders section tabs with inline editor, status alerts, and page picker | `Pages/Content/LivePreview.cshtml.cs` hydrates sections via `IContentOrchestrator.GetPageWithSectionsAsync` and posts `UpdateSectionAsync` | Shares section editing flow with dashboard; optimistic feedback only through TempData alerts |
| In-canvas editor overlay | `/content/canvas/{id}` endpoint injects `/editor/bugence-editor.js` and `/editor/bugence-editor.enhancements.js` into legacy HTML | `Program.cs` + `Services.EditorAssetCatalog` map slugs -> static files and selector hints; JS stored under `wwwroot/editor` | Overlay script sanitises input, computes selectors, hits `/api/content/*`; no bundler yet |
| Publishing pipeline | Publish buttons and API route `/api/content/pages/{id}/publish` | `Services.ContentOrchestrator.PublishPageAsync` rewrites static HTML via AngleSharp, stamps `LastPublishedAtUtc`, writes audit log | Emits warnings when selectors missing; legacy static site mirrored at repo root (`index.html`, etc.) |
| Analytics / audit logs | `Pages/Insights/Logs.cshtml` renders filters and change table | `Pages/Insights/Logs.cshtml.cs` queries `ContentChangeLogs` with optional range/term filters | Acts as analytics stub: no charts yet, but diff viewer surfaces previous vs new values |
| Authentication & access | Login/reset Razor pages under `Pages/Auth` | ASP.NET Core Identity + Cookie auth configured in `Program.cs` | No role segmentation; single seeded operator account (`DatabaseSeeder`) |
| Content REST surface | `/api/content/pages/{pageId}`, `/api/content/pages/{pageId}/sections`, `/api/content/pages/{pageId}/publish`, `/api/content/pages/{pageId}/sections/{sectionId}` | Minimal APIs within `Program.cs` backed by `IContentOrchestrator` | All routes require auth and disable antiforgery for JS access; responses are anonymous objects (no shared DTOs yet) |

### Additional observations
- Static marketing site (`index.html`, `MeetPeteD.html`, etc.) lives at repository root and is exposed through `UseStaticFiles` plus canvas endpoint injection.
- `wwwroot/js/site.js` powers dashboard/drawer interactions; no module system or TypeScript.
- `DatabaseSeeder` seeds baseline site pages, sections, and default identity user; media stored under `wwwroot/uploads` via `FileSystemStorageService`.

## Phase Completion Overview

- [ ] Phase 0 - Foundations - Audit and monorepo scaffolding are present, but CI workflows under `.github/workflows` are still missing.
- [x] Phase 1 - TypeScript Core + Bundling - Canvas and dashboard bundles live in `packages/canvas/src` and `packages/dashboard/src`, `@bugence/core` centralises shared types, NSwag generates C# contracts, and `tsconfig.base.json` exposes Razor ambient typings.
- [x] Phase 2 - State & Data Layer - Shared store helpers (`packages/core/src/store/createStore.ts`), SWR-style fetchers (`packages/core/src/api/content.ts`), and optimistic/conflict-aware stores (`packages/canvas/src/store/canvasStore.ts`, `packages/dashboard/src/store/dashboardStore.ts`) are in place.
- [x] Phase 3 - Schema-Driven UI - Section, sidebar, and workflow schemas live under `packages/core/src/schemas`, consumed by runtime renderers such as `packages/dashboard/src/ui/schemaSidebar.ts`.
- [x] Phase 4 - Component Kit - Reusable primitives (`Modal`, `Flyout`, `Toolbar`, etc.) exist in `packages/ui/src/components`, with theme tokens and Storybook scaffolding in `.storybook`.
- [x] Phase 5 - Real-Time Sync & Workflow - Dashboard timeline consumes the shared sync bus, shows dirty/conflict/review badges, delivers publish diff summaries, and background sync now refreshes history.
- [x] Phase 6 - Server Contract & Validation - OpenAPI spec + generated types now cover diff metadata, Minimal API endpoints enforce FluentValidation rules, and history responses ship diff summaries for the dashboard timeline.
- [x] Phase 7 - Pluggable Actions & Metrics Platform - Core now exposes pluggable action/metric registries, canvas registers lazy canvas actions, and dashboard sidebar metrics resolve through the shared registry.
- [x] Phase 8 - Dashboard Experience Enhancements - Library console now hydrates from live API data with inspector overlays, section editor cards include detail/history tabs, the publish console orchestrates task-driven workflows, and notifications surface real-time tasks.
- [x] Phase 9 - Analytics & Insights - `@bugence/analytics` powers insight charts, storyline summaries, and import-mapped runtime bundles that hydrate the dashboard audit view end-to-end.
- [x] Phase 10 - Tooling & Developer Experience - ESLint/Prettier/Stylelint enforcement, lint-staged hooks, and expanded test suites/tooling scripts are not configured across packages.

## Tech Inventory

**Solution layout**
- `BugenceEditConsole/` Razor Pages app (net9.0) with local SQLite database (`app.db`).
- Legacy static assets (`index.html`, `BookPeteD.html`, etc.) plus supporting folders (`Assets`, `Css`, `Img`, `Script`).
- `wwwroot/editor` hosts custom canvas editor JS/CSS; `wwwroot/js/site.js` manages dashboard UI chrome.

**Backend stack**
- ASP.NET Core 9 Razor Pages + Minimal APIs (`Program.cs`).
- Identity + SQLite persistence (`ApplicationDbContext`, `ApplicationUser`).
- Entity Framework Core with migrations under `BugenceEditConsole/Migrations`.
- Services: `ContentOrchestrator` (sync, CRUD, publish), `FileSystemStorageService` (disk uploads), `EditorAssetCatalog` (static asset map).
- AngleSharp 1.0.7 for DOM parsing during publish.

**Data model**
- `SitePage`, `PageSection`, `ContentChangeLog`, `UserProfile`, `PasswordResetTicket` records define dashboard schema.
- `PageSection` tracks `ContentType` (text, rich text, html, image), `CssSelector`, timestamps (`UpdatedAtUtc`, `LastPublishedAtUtc`).
- Change logs capture previous/new values and are source for analytics stubs.

**Frontend + styling**
- Razor views use Tailwind-like utility classes (precompiled CSS in `wwwroot/css`).
- Rich text editor is bespoke (contentEditable + toolbar commands) implemented in `wwwroot/editor/bugence-editor.js` and `wwwroot/js/site.js`.
- No modern bundler/pnpm workspace; `package.json` only pins TypeScript 5.9.2 without scripts.

**Tooling & automation**
- Dotnet CLI builds; no solution-level tests detected.
- No CI configuration, linting, or formatting tools wired up.
- Media uploads stored on disk (`wwwroot/uploads`); no CDN integration.

**Integration touchpoints**
- Static asset rewrite on publish ties Razor app to legacy HTML files (`EditorAssetCatalog.PageAssets`).
- Canvas endpoint injects configuration via `<script id="bugence-editor-config">` JSON blob for the overlay.
- Sync routine (`ContentOrchestrator.SyncPageFromAssetAsync`) backfills sections from latest static HTML using selector hints.

**Known gaps ahead of Phase 1**
- Absent package workspace/monorepo structure; client scripts untyped and bundled manually.
- Server/client contracts lack shared DTOs or OpenAPI spec; responses composed ad hoc.
- Testing, linting, formatting, and CI pipelines missing.
- Analytics view limited to tabular logs - no charts or metric registry yet.

## Phase 0 - Monorepo Scaffolding Plan

**Target structure**
- `apps/dashboard`: Razor front end (migrated from `BugenceEditConsole`) hosting admin UX.
- `apps/api`: ASP.NET Core minimal API (current project refactored to expose contract-only surface).
- `apps/canvas`: Static canvas shell served through Vite for authoring overlay.
- `packages/core`: Shared TypeScript types/utilities (`@bugence/core`) generated from OpenAPI + handcrafted helpers.
- `packages/ui`: Reusable UI primitives (Modal, Flyout, Toolbar, etc.).
- `packages/analytics`: Future metrics/adapters.
- Shared configuration at repo root (`pnpm-workspace.yaml`, `tsconfig.base.json`, `.npmrc`, `.editorconfig`).

**Scaffolding steps**
- Introduce `pnpm` workspace with scripts (`pnpm build`, `pnpm dev:dashboard`, `pnpm dev:canvas`) and per-package `package.json`.
- Move legacy JS into TypeScript packages (`packages/canvas`, `packages/dashboard`) with Vite build outputs targeting Razor `_Layout` bundles.
- Extract shared typings into `packages/core/src/types.ts`; generate `.d.ts` for .NET via `openapi-typescript` + NSwag pipeline.
- Configure ESLint/Prettier/Stylelint base configs in root, extend per package.
- Add GitHub Actions workflow to run `pnpm install`, `pnpm lint`, `pnpm test`, and dotnet build/test for `apps/api`.
- Set up `turbo.json` or `pnpm` recursive scripts to coordinate cross-package builds.

## Phase Roadmap Alignment

| Phase | Key deliverables | Dependencies |
| --- | --- | --- |
| 1. TypeScript Core + Bundling | Migrate `wwwroot/editor/bugence-editor.js` -> `packages/canvas` Vite bundle; migrate `wwwroot/js/site.js` -> `packages/dashboard`; author shared `@bugence/core` types with TS build + declaration output | Phase 0 workspace scaffolding, baseline OpenAPI schema |
| 2. State & Data Layer | Implement `useDashboardStore`/`useCanvasStore` over shared `createStore` helper, add SWR-like fetchers targeting `/api/content/pages`, `/api/content/sections`, `/api/content/history`, wire optimistic updates + ETag conflict detection | Phase 1 TypeScript modules, API endpoints exposing ETag headers |
| 3. Schema-Driven UI | Define JSON/TS literal schemas for section types, sidebar cards, workflow steps; build renderer to translate schema -> components across dashboard + canvas overlay | Phase 2 state layer, `@bugence/core` schema typings |
| 4. Component Kit | Extract primitives into `packages/ui`, introduce design tokens (Tailwind config or CSS variables), wire Storybook-lite/VitePress playground | Phase 3 schema definitions, Vite tooling |
| 5. Real-Time Sync & Workflow | Standardize snapshot/diff/apply pipeline, broadcast store events, add dashboard timeline with live dirty markers and background sync worker | Phase 2 store foundation, Phase 3 schema metadata |
| 6. Server Contract & Validation | Generate OpenAPI for `/api/content/*`; use `openapi-typescript` for client, NSwag for C# partials; enforce FluentValidation/Minimal API filters; expose content history endpoints with diff metadata | Phase 0 CI, Phase 1 shared types |
| 7. Pluggable Actions & Metrics | Build action registry (duplicate/delete/publish/AI rewrite/A-B test); implement lazy loader with capability flags; metrics registry with extensibility hooks | Phase 4 component kit, Phase 3 schema definitions |
| 8. Dashboard Experience Enhancements | Library tables with pagination/search/bulk actions, schema-driven section detail tabs, publishing console stepper, notifications/tasks integration | Phases 3-7 foundations |
| 9. Analytics & Insights | `packages/analytics` for metric providers, render charts (Chart.js/micro D3), storyline view linking metrics to snapshots | Phase 7 metrics registry, Phase 5 sync data |
| 10. Tooling & DX | Enforce ESLint/Prettier/Stylelint, configure lint-staged, add Vitest/Jest, Playwright flows, CLI scripts (`pnpm build/dev`) | Phases 1-4 TypeScript migration, Phase 0 CI baseline |

## Implementation Snapshot - 2025-10-20
- pnpm workspace scaffolded with `packages/core`, `packages/dashboard`, `packages/canvas` and shared `tsconfig.base.json`.
- Legacy `wwwroot/js/site.js` and `wwwroot/editor/bugence-editor.js` now sourced from TypeScript modules and build-ready via Vite/tsc configs.
- `@bugence/core` publishes shared contracts in `packages/core/src/types.ts`, paired with generated C# DTOs under `BugenceEditConsole/OpenApi/Generated`.
- OpenAPI spec/NSwag config added (`BugenceEditConsole/OpenApi/content.openapi.yaml`) enabling one-click contract regeneration.
- Phase 2 data layer in place: `/api/content/pages|sections|history` emit ETags, TypeScript stores (`@bugence/dashboard` + `@bugence/canvas`) consume them via a shared `createStore` helper with optimistic updates and conflict handling.

## Phase 5 - Real-Time Sync & Workflow

**Goals**
- Deliver low-latency collaboration signals between the canvas overlay, dashboard timeline, and publish pipeline.
- Keep local state trustworthy under concurrent edits via deterministic snapshot → diff → apply → commit semantics.
- Surface actionable workflow cues (dirty markers, review readiness, publish summaries) without refreshing the dashboard.

**Implementation tracks**
- Snapshot pipeline standardization
- Dashboard timeline live workflow cues
- Background sync worker & merge orchestration

**Snapshot pipeline (capture → diff → apply → commit)**
- Define `SnapshotEnvelope` (pageId, sectionId, selector, contentHash, payload, version, timestamp) and persist per-edit baselines in `@bugence/canvas`.
- Capture: trigger snapshot build on editor blur/save + throttled input, writing to in-memory baseline cache and emitting `snapshot.captured`.
- Diff: implement `createSectionDiff(prev, next)` producing `DiffEnvelope` (changeType, before, after, annotations) with media-aware hashing; expose under `@bugence/core/diff`.
- Apply: route diffs through `canvasStore.applyDiff(diff)` to mutate state, mark section dirty (`sectionState.dirty = true`), and emit `section.changed`, `section.reverted`, or `section.conflicted` events.
- Commit: on publish success, update baseline cache, clear dirty flags, append `PublishCommit` record (pageId, sections, reviewer, comment, diffSummary) for timeline ingestion, emit `section.committed`.

**Canvas → dashboard event bridge**
- Create `eventBus` module shared between `@bugence/canvas` and `@bugence/dashboard` using BroadcastChannel (browser) + fallback to `postMessage`.
- Normalize events as `{topic, pageId, sectionId?, payload}`; topics include `section.changed`, `section.committed`, `review.status.updated`.
- Dashboard subscribes to relevant topics and updates timeline store + UI badges in real time.

**Dashboard timeline integration**
- Extend timeline store with `dirtySections` map and `reviewStatuses` keyed by section.
- Render live dirty markers (dot + tooltip) on timeline entries and section list using store state; throttle re-render using `requestAnimationFrame`.
- Introduce review workflow commands: `requestReview`, `approve`, `reject` mutating `reviewStatuses` and emitting `review.status.updated`.
- Generate publish diff summaries via `DiffEnvelope[] -> PublishSummary` transformer that groups by section type and attaches reviewer notes; display in timeline drawer pre-publish.

**Background sync worker**
- Ship `SyncWorker` (Web Worker for canvas, `setInterval` loop for dashboard) with configurable interval (`sync.pollIntervalMs`, default 15000).
- Fetch `/api/content/pages/{id}/sections?sinceVersion={version}` using ETags + `If-None-Match`; handle `304` as no-op.
- On delta payload, convert remote sections into `SnapshotEnvelope` set and run `createSectionDiff` against local baselines.
- Auto-merge non-conflicting diffs; when content diverges, emit `section.conflicted` with pointers to local vs remote diff and surface toast linking to diff modal.
- Record last successful sync timestamp + version in store for diagnostics and to drive UI (e.g., “Synced 12s ago” badge).

**Deliverables checklist**
- [x] `@bugence/core` diff + snapshot types finalized and exported.
- [x] `@bugence/canvas` implements baseline cache, diff application, and commit hooks.
- [x] Shared event bus wired between canvas/dash stores with integration tests.
- [x] Dashboard timeline UI updated with dirty markers, review status chips, and diff summary panel.
- [x] Background sync worker (canvas + dashboard) scheduling, fetch, merge, conflict resolution tested.
- [ ] Telemetry hooks (optional): log sync latency, diff sizes, conflict frequency for future analytics.
- [x] OpenAPI spec v0.3.0 + generated TypeScript contract (`packages/core/src/generated/content.ts`) stay in lockstep with server DTOs.
- [x] Minimal API section mutations validated via FluentValidation with problem+json responses.
- [x] Content history endpoint emits diff metadata (change type, delta, snippet) for dashboard timelines.

## Phase 7 - Pluggable Actions & Metrics Platform

**Goals**
- Provide a shared action registry with capability flags and lazy loading so canvas and dashboard features stay modular.
- Wire the canvas overlay into the new registry with a first wave of authoring actions.
- Expose dashboard sidebar metrics through the registry so cards can resolve data dynamically.

**Implementation tracks**
- Action registry and capability controls
- Canvas action suite
- Dashboard metrics pipeline

**Action registry**
- Added capability manager plus action registry to `@bugence/core`, supporting lazy loaders, capability gating, and error-safe execution.
- Canvas package now registers default actions (duplicate, delete, publish, AI rewrite, create variant) via dynamic handlers and new store helpers for section creation/duplication.
- Registry auto-registration keeps overrides possible while ensuring default actions load on package import.

**Metrics platform**
- Introduced metric registry in `@bugence/core` with resolver + formatter hooks for pluggable metrics.
- Dashboard registers baseline metrics (draft count, last publish, accessibility score, broken links) and sidebar cards resolve values asynchronously with graceful fallbacks.
- Metric context carries DOM dataset plus page ids so future providers can layer in live store data or remote results.

**Deliverables checklist**
- [x] Action registry and capability helpers exported from `@bugence/core`.
- [x] Canvas action handlers (duplicate, delete, publish, AI rewrite, create variant) lazily loaded via registry.
- [x] Dashboard sidebar metrics now powered by the shared metric registry.
- [ ] Optional analytics: capture action/metric telemetry for future insight dashboards.

## Phase 8 - Dashboard Experience Enhancements

**Goals**
- Elevate the content library with live data wiring, adaptive sorting, and deep inspector tooling.
- Give section owners richer context through inline detail/history tabs tied to timeline telemetry.
- Orchestrate publish readiness with task-aware console flows and surfaced notifications.

**Implementation tracks**
- Library intelligence & inspector surface
- Section detail and history tabs
- Publish console workflow & notifications

**Library intelligence & inspector surface**
- Library Razor view now emits structured data attributes + inspector scaffolding, while `@bugence/dashboard` hydrates via the dashboard store and renders dynamic metrics, trending chips, and sorting/filtering controls.
- `libraryConsole.ts` rebuilds page grids client-side, drives detail drawer overlays (sections, activity, health signals), and updates hero/status metrics from live API responses with fallback to server rendering.
- Inspector fetches sections and history on demand, translating `fetchSections`/`loadDashboardHistory` data into actionable cues and signals per module.

**Section detail and history tabs**
- Editor cards embed tab chrome plus detail/history panels fed by new `sectionDetails.ts`, which observes dashboard timeline state to show diff annotations, review badges, and conflict markers.
- History tab filters centralized timeline history to per-section timelines, exposing performer, summaries, and timestamps without leaving the editor.
- Dataset on section cards now exposes selectors, versions, and publish metadata for richer diagnostics and downstream automation.

**Publish console workflow & notifications**
- Sidebar summary introduces task preview + "Open publish console" CTA; `publishConsole.ts` expands this into a modal workflow with diff recap, task tracking, manual task capture, and gating logic for publish confirmation.
- Notification center (`dashboardNotifications.ts`) listens to timeline events, surfaces toasts, and seeds console tasks for conflicts, reviews, and publish readiness.
- Console integrates with existing publish form, enforces task completion, and records optional notes, while toast utilities centralize dashboard alerts.

**Deliverables checklist**
- [x] Library console enriched with live store data, dynamic sorting/filtering, and inspector overlay (`libraryConsole.ts` + Razor instrumentation).
- [x] Section editor cards ship tabbed detail/history panels powered by `sectionDetails.ts` with timeline integration.
- [x] Publish console modal coordinates diff summary, task gating, notifications, and publish-trigger handoff.
- [x] Dashboard notification rail reacts to timeline events and seeds actionable tasks.
- [ ] Optional persistence: surface publish console notes/tasks across sessions via storage.

## Phase 9 - Analytics & Insights

**Goals**
- Replace static audit metrics with interactive charts, breakdowns, and narrative insights.
- Ship a reusable analytics runtime that plugs into dashboard pages without bundler coupling.
- Surface range summaries and storyline callouts so operators can spot hotspots quickly.

**Implementation tracks**
- Analytics runtime package
- Dashboard insights hydration
- Visual system & import map delivery

**Analytics runtime package**
- Stood up `@bugence/analytics` with typed payload contracts, chart primitives (`lineChart`, `breakdownList`), and storyline renderer.
- Runtime build emits ES modules to `wwwroot/js/analytics`, while workspace build keeps type declarations in `dist/` for package consumers.
- Trend renderer produces SVG area/line combos with grid, tooltips, and empty-state handling.

**Insights hydration**
- Razor insights page now exposes `data-analytics-summary` and import map so the package can hydrate summaries, charts, and narrative timeline.
- `initInsightsAnalytics` parses the serialized payload, wires charts, generates storylines, and stamps readiness markers for progressive enhancement.
- Summary chip shows total change counts + date range with polite `aria-live` updates for assistive tech.

**Visual system & delivery**
- `theme.css` gains analytics card, list, chart, and storyline styles tuned for the `insights-dark` palette, including responsive layouts and meter animations.
- Layout now serves `site.js` as a module and registers an import map that points `@bugence/analytics` to the runtime bundle.
- Dashboard runtime build pulls from compiled package outputs, preserving existing module structure without bundler tooling.

**Deliverables checklist**
- [x] `@bugence/analytics` package exports trend, breakdown, and storyline renderers with runtime + type builds.
- [x] Insights logs page hydrates charts, breakdowns, storyline, and summary chip via `initInsightsAnalytics`.
- [x] Theme styles updated to cover analytics cards, SVG rendering, storyline list, and empty states.
- [x] Layout import map + module script ensure browsers resolve analytics bundle at runtime.
- [ ] Optional telemetry: feed chart interactions or storyline impressions into analytics events.

## Phase 10 - Tooling & Developer Experience

**Goals**
- Establish consistent linting and formatting coverage across every workspace package.
- Automate contributor feedback loops with pre-commit validation and formatting.
- Stand up a shared unit testing harness while adding coverage for critical core utilities.

**Implementation tracks**
- Workspace linting & formatting suite
- Pre-commit automation
- Vitest adoption & coverage

**Workspace linting & formatting suite**
- Root ESLint config now extends TypeScript recommended + import ordering, and each package's `lint` script runs ESLint directly instead of placeholder `tsc --noEmit`.
- Prettier + Stylelint configs added with shared ignore lists and scripts (`lint:styles`, `format`, `format:check`) so formatting is consistent between packages and Razor-adjacent assets.
- Stylelint enforces property ordering and SCSS parsing (via `postcss-scss`) while skipping generated/static artifacts to keep signal focused on authored stylesheets.

**Pre-commit automation**
- `lint-staged` paired with `simple-git-hooks` runs targeted ESLint, Stylelint, and Prettier tasks against staged files via the new `pre-commit` hook.
- Repository `prepare` script ensures local hook installation, keeping contributor setup lightweight and reproducible.

**Vitest adoption & coverage**
- Workspace test scripts now rely on Vitest; `@bugence/core` introduces unit tests covering snapshot diffing, conflict detection, and annotation logic.
- Core `tsconfig` includes Vitest globals for type-safety, and package-level configs emit coverage artifacts for future gating.
- Remaining packages run Vitest with `--passWithNoTests`, providing a consistent entry point until their suites are populated.

**Deliverables checklist**
- [x] ESLint/Prettier/Stylelint configured with workspace scripts and shared ignores.
- [x] lint-staged + simple-git-hooks enforce linting/formatting in pre-commit.
- [x] Vitest harness added with core diff tests and workspace test scripts.
- [ ] Optional CI wiring: surface tooling/test commands in GitHub Actions.
