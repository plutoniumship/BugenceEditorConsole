# IIS Deployment Checklist (Domains + Preflight)

Use this checklist for production deployments to avoid stale binaries and runtime config drift.

## 1) Publish to a single output folder

Use one explicit output path and do not publish into an existing `publish` folder recursively.

```powershell
dotnet publish .\BugenceEditConsole\BugenceEditConsole.csproj -c Release -o C:\deploy\BugenceEditConsole
```

## 2) Sync files to IIS site root

Copy `C:\deploy\BugenceEditConsole\*` to the IIS site physical path used by the app.

- Do not copy nested `publish\publish\...` trees.
- Confirm `BugenceEditConsole.dll`, `appsettings.Production.json`, and `web.config` are in the site root.

## 3) Restart app runtime

Recycle the app pool (or restart IIS site/app pool) after file sync.

## 4) Verify environment and settings

Confirm deployed runtime values:

- `ASPNETCORE_ENVIRONMENT` is set to `Production`.
- Effective `DomainRouting` has:
  - `AutoManageIisBindings=true`
  - `PerProjectIisSites=true`
  - `CertificateStoreName=My`
  - `CertificateStoreLocation=LocalMachine`

## 5) Verify deployed build version

After restart, open:

`GET /api/system/version` (authorized session required)

Confirm it returns current build metadata (`assemblyVersion`, `informationalVersion`, `startedAtUtc`, `environment`).

## 6) Warm-up and smoke tests

1. Open Domains page.
2. Run one `Refresh` on a failing custom domain.
3. Run `Preflight`.
4. Confirm:
   - no HTML `/Error` page for API calls,
   - preflight returns JSON,
   - refresh does not fail with stale IIS binding collisions.
5. For custom-domain SSL issuance, confirm HTTP-01 challenge path serves extensionless files:
   - `http://<custom-domain>/.well-known/acme-challenge/<token>`
   - expected: `200` (not `404`).
