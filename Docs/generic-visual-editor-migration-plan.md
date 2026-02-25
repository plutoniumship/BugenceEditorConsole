# Generic Visual Editor Migration Plan

## Objectives
- Replace any fixed-site experience with a generic flow: dashboard sidebar adds an **Upload Static Site** entry that opens a dedicated upload page, accepts a project bundle, and auto-prepares it so running the app lands directly on the uploaded site’s index page at `/` and `/login` opens the editor.
- Preserve full-site editing (text, links, images, assets) across multi-page or SPA bundles.
- Make the pipeline repeatable, secure, and observable so new projects can be onboarded without code changes.

## Success Criteria
- `/` serves the active uploaded project’s `index.html`; all internal links and assets resolve without manual fixes.
- `/login` opens the visual editor scoped to the active project, with full in-place editing of text/href/src/background images.
- Upload → validate → process → view → edit → publish → rollback works end-to-end for arbitrary static bundles.
- Versioning and audit logs exist for publishes; rollbacks restore a prior snapshot cleanly.
- Security: uploads constrained by size/type, assets are same-origin, CSP enforced, and only authorized users can edit/publish.

## Guiding Principles
- Be project-agnostic: no selectors, IDs, or routes tied to any specific site.
- Non-destructive processing: keep original upload, generate a processed copy with injected editor hooks.
- Clear separation: upload/processing, serving/routing, editing/publishing, and observability are distinct layers.
- Fail loudly with actionable errors (missing entry, broken links, disallowed assets).

## Architecture Outline
- **Project model:** id, owner, status, entrypoint, discovered pages, asset manifest, active flag, versions, audit log.
- **Storage layout:** `uploads/<project>/<timestamp>.zip`, `workspace/<project>/source`, `workspace/<project>/processed`, `workspace/<project>/versions/<versionId>`.
- **Processing pipeline:** upload → (optional scan) → unzip → validate → rewrite (paths + editor hooks) → manifest → publish pointer update.
- **Routing:** `/` serves the active project’s processed bundle; `/login` boots the editor with project context; optional project switcher in dashboard.
- **Editing model:** in-place DOM edits to text/href/src/backgrounds; asset uploads routed to project storage; save as drafts; publish writes processed files and snapshots.

## Phased Delivery Plan
1) **Discovery & Decisions**
   - Audit dashboard, routes, editor integration points, and legacy site-specific hooks.
   - Decide accepted bundle format (zip), size/type caps, and allowed external resources.
   - Output: architecture decision record, constraints, and migration checklist.
2) **Upload Flow & Validation**
   - Dashboard UI for uploading a zip + selecting entry (default `index.html`); show limits.
   - Backend: accept upload, validate structure, enforce caps, reject disallowed content.
   - Output: upload endpoint/UI, structured error messages, basic tests.
3) **Processing & Rewrite Pipeline**
   - Unzip to workspace; build asset manifest.
   - Rewrite HTML: normalize links, adjust asset URLs to hosted path, inject editor hooks (data attrs + boot script).
   - Multi-page support: discover internal links, ensure navigation stays scoped; SPA fallback if applicable.
   - Output: processing job with logs, manifest, processed bundle.
4) **Serving & Routing**
   - Configure server to serve the active processed bundle at `/`.
   - `/login` loads editor with active project context and page list; add project switcher if multiple projects supported.
   - Output: routing updates, active-project selector, smoke tests for `/` and `/login`.
5) **Editor Generalization**
   - Remove any site-specific selectors; use generic DOM targeting for text/href/src/background images.
   - Resource panel: list pages/assets; quick navigation; highlight current page.
   - Save → draft; Publish → write processed files; undo/redo scoped per page.
   - Output: editor that works on arbitrary bundles; regression tests on sample sites.
6) **Persistence, Versioning, Rollback**
   - Snapshot on publish; store diff/metadata (who/when/what).
   - Rollback to previous snapshot; log audit events.
   - Output: version store, rollback API/UI, audit log.
7) **Security & Isolation**
   - CSP for served bundle; same-origin asset enforcement; optional sandbox for risky content.
   - Validate uploads (mime/size), optional AV scan, block remote scripts if disallowed.
   - AuthZ: owner/editor roles; public view read-only.
   - Output: security checks, CSP config, auth rules.
8) **Testing & QA**
   - Automated: pipeline unit tests (rewrite, manifest), integration (upload→serve→edit→publish), routing for `/` and `/login`.
   - Manual matrix: multi-page site, SPA, missing assets, image swap, link edits, rollback, large bundle.
   - Output: test suite, QA checklist, performance baseline.
9) **Migration Off Legacy**
   - Remove legacy hardcoding from routes/config.
   - Re-import a sample legacy bundle via new flow to prove parity; fix gaps found.
   - Output: parity report and fixes.
10) **Docs, Monitoring, Launch**
    - Operator guide (upload, set active, publish/rollback); developer guide (extending pipeline/editor).
    - Monitoring: processing failures, broken links, publish errors, storage usage.
    - Launch checklist and rollback plan.

## Key Workstreams (Cross-Phase)
- **HTML/Asset Rewrite**
  - Make URLs relative to hosted base; ensure index at `/`.
  - Inject minimal editor hooks to avoid layout shifts.
  - Handle edge cases: query-string assets, hash links, SPA 404 fallback, font paths.
- **Editing Capabilities**
  - Text/href/src/background image editing; URL validation; image upload → project storage.
  - Draft vs publish separation; change history per page; visual cues for editable regions.
- **Versioning & Storage**
  - Store both original and processed bundles; snapshot per publish.
  - Rollback restores processed bundle and updates active pointer atomically.
- **Security**
  - CSP tuned for uploaded bundle; forbid mixed content; same-origin policy for assets.
  - Input validation for URLs; optional script sanitization; audit trail for edits/publishes.

## Risks & Mitigations
- **Untrusted content executes:** Enforce CSP, same-origin assets, optional sandbox; validate uploads before serve.
- **Broken links/assets after rewrite:** Automated link/asset checks in pipeline; fail fast with diagnostics.
- **Large bundles slow processing:** Set size caps; stream unzip; background jobs with progress UI.
- **Editor fails on unusual DOM:** Use generic selectors; fallback to source edit for nonstandard nodes.
- **Rollback complexity:** Atomic snapshot switch; keep immutable versions; test rollback paths.

## Launch Checklist (must-pass)
- Legacy-specific code/paths removed; active-project routing in place.
- Upload → process → view at `/` succeeds for sample multi-page and SPA bundles.
- `/login` loads editor scoped to the active project; edits to text/links/images publish correctly.
- Assets and links resolve; no mixed-content or cross-origin asset pulls.
- Publish creates a snapshot; rollback restores previous state cleanly; audit log records actions.
- Tests green; monitoring dashboards/alerts live; operator + developer docs published.
