# Local operator guide

This is the authoritative local operating path for Photo Identity Indexer on Windows. It distinguishes the current permanent-archive workflow from retained historical migration/evaluation evidence.

## Current catalogue authority

PostgreSQL is the single supported and authoritative catalogue. WI-0102 completed the maintainer production migration/cutover and rollback acceptance, WI-0147 made PostgreSQL unconditional at runtime, and WI-0149 removed the former SQLite implementation and executable compatibility paths. `/health` must report `catalogueProvider: postgresql`.

The final stopped-source SQLite backup from WI-0102 may be preserved unchanged as historical migration/rollback evidence, but current Photo Identity binaries do not open it. Do not configure a catalogue provider or SQLite database path. Historical rollback instructions remain in [PostgreSQL catalogue migration and cutover](postgresql-catalogue-cutover.md) only as evidence of the accepted migration boundary.

Check the short [`BUILD_CONTEXT.md`](../../BUILD_CONTEXT.md) handoff and the canonical [work-item registry](../delivery/status/work-items.yaml) before treating a planned feature as available.

## Trust and privacy boundary

- Keep personal photos, PostgreSQL data, crops, embeddings, proxies and private reports outside the repository.
- Keep PostgreSQL data and backups on local/private storage, not a synchronised cloud folder.
- Treat the Windows computer as the trusted control plane.
- Original photos are read-only inputs.
- The browser application is unauthenticated. Prefer localhost; use another device only on a trusted private network with narrow firewall scope.
- Personal OneDrive is accessed through the Windows sync client, not Microsoft Graph.

## 1. Build and verify the application

From the repository root:

```powershell
./build.ps1
./test.ps1
./verify-local.ps1 -InstallModels
./models/install-models.ps1 -Id centerface-2019-fp32
./verify-review.ps1 -Mode Smoke -Configuration Release
```

PostgreSQL-specific live acceptance is available through:

```powershell
./verify-postgres.ps1
```

The permanent archive analysis profile is governed separately from generic evaluation defaults. Use the currently accepted values recorded by the bounded archive/detector work items rather than inventing production settings.

## 2. Choose permanent local paths

Use local non-OneDrive paths for governed application artefacts such as model/analysis output, review proxies and backups. PostgreSQL catalogue storage is owned by the configured local PostgreSQL service.

Example layout:

```powershell
$root = "C:\PhotoIdentity"
$analysis = Join-Path $root "analysis"
$proxies = Join-Path $root "review-proxies"
$publish = Join-Path $root "app"
$backups = Join-Path $root "backups"

New-Item -ItemType Directory -Force -Path $root,$analysis,$proxies,$backups | Out-Null
```

The actual Personal OneDrive archive root and PostgreSQL connection string are private configuration and must not be committed to Git.

## 3. Configure PostgreSQL and bounded archive storage

The API host requires `PhotoIdentity:Postgres:ConnectionString`. For normal packaged use, the launcher stores only the **name** of the environment variable containing that connection string; the secret itself must not be stored in launcher JSON.

Archive operation also uses the accepted analysis/proxy/hydration settings. Environment-variable examples include:

```powershell
$env:PhotoIdentity__ArchiveAnalysisOutputRoot = $analysis
$env:PhotoIdentity__ReviewProxyRoot = $proxies
$env:PhotoIdentity__ReviewProxyProfileId = "<accepted-profile-id>"
$env:PhotoIdentity__ReviewProxyMaximumLongEdge = "<accepted-max-edge>"
$env:PhotoIdentity__ReviewProxyJpegQuality = "<accepted-jpeg-quality>"
$env:PhotoIdentity__ArchiveHydration__MinimumFreeSpaceReserveBytes = "<accepted-reserve>"
$env:PhotoIdentity__ArchiveHydration__MaximumManagedHydrationBytes = "<accepted-budget>"
$env:PhotoIdentity__ArchiveHydration__MaximumConcurrentOperations = "<accepted-concurrency>"
```

Do not invent production values. Use values accepted through [bounded archive acceptance](bounded-archive-acceptance.md). See [review-proxy serving and bounded originals](review-proxy-serving.md) for exact semantics.

PostgreSQL is unconditional: `PhotoIdentity__CatalogueProvider` and `PhotoIdentity__DatabasePath` are obsolete settings and are rejected by the packaged launcher.

## 4. Install and run the Windows application

Normal operation uses the self-contained `win-x64` operator package. Build it with:

```powershell
./Package-PhotoIdentity.ps1 -Configuration Release
```

The default ZIP is:

```text
.artifacts\packages\PhotoIdentity-win-x64.zip
```

Extract the complete ZIP to a local code-only folder such as `C:\Apps\PhotoIdentity-<version>`, then start:

```text
PhotoIdentity.cmd
```

The launcher:

- accepts only a loopback HTTP URL such as `http://127.0.0.1:5080`;
- opens the browser only after `/health` reports the application ready;
- reuses an already healthy Photo Identity instance instead of starting a duplicate;
- refuses to start when the configured port is occupied by another process;
- resolves the private PostgreSQL connection string from the configured environment-variable name; and
- writes startup stdout/stderr logs under `%LOCALAPPDATA%\PhotoIdentity\launcher-logs` when troubleshooting is needed.

The package directory contains replaceable application code only. Keep PostgreSQL data, analysis output, proxies, launcher configuration and backups outside it. See [Windows operator package](windows-package.md) for the complete package/update procedure.

Manual framework-dependent publishing remains available for development and diagnostics:

```powershell
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish `
  .\src\PhotoIdentity.Api\PhotoIdentity.Api.csproj `
  --configuration Release `
  --output $publish
```

## 5. Configure and synchronize permanent archive coverage

Use the **Archive** page for production archive coverage and synchronization. The application operates through PostgreSQL only; the former `archive --database ...` SQLite CLI paths were retired under WI-0149.

Expanding coverage keeps one permanent archive source identity. A broader included parent can subsume previously listed children without creating another source authority.

Synchronization revisits included coverage for new, changed, missing and newly available files while avoiding repeated unchanged exact-profile work. Browser-triggered long-running synchronization is server-owned and durable; see [Archive background synchronization](archive-background-sync.md).

## 6. Advance the archive

Use **Advance archive** in the Archive page for the bounded permanent workflow. It coordinates source verification, managed OneDrive hydration, governed analysis, durable proxy generation and release/retry behavior through PostgreSQL.

Important distinctions:

- OneDrive availability and source verification are separate states.
- Metadata changes can require verification but never establish an immutable revision by themselves.
- Authoritative SHA-256 bytes establish or reselect revisions.
- First-time online-only sources may need temporary bounded hydration before their first revision exists.
- Ordinary collection browsing uses proxies and must not hydrate originals.
- Explicit original viewing uses the separate hydrate/status/view/release lifecycle.

## 7. Review, collections and metadata

Use the browser application for canonical review/identity decisions, people maintenance, Smart/Creative Collections, metadata/Places, archive review and slideshows. The browser/runtime shares one PostgreSQL catalogue; there is no provider-specific alternative path.

Normal thumbnails/previews are served from durable review proxies when configured. Request authoritative full-resolution content only through the explicit original lifecycle so immutable revision checks and managed hydration/release accounting remain enforced.

## 8. CLI engineering/evaluation operations

The CLI remains available for supported PostgreSQL-backed and provider-neutral engineering tasks. Current help is authoritative:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- --help
```

Catalogue commands that require a database use a PostgreSQL connection string from an explicitly named environment variable. `bundle process` remains database-free. Migration-era SQLite backup/migrate/archive/batch/match/evaluation-export and bundle import/export commands are no longer supported.

## 9. Back up and restore

Use [PostgreSQL production operations](postgresql-operations.md) for routine logical backup, isolated restore verification, restart and upgrade procedures. The old SQLite backup is not a current recovery target for the application after WI-0149.

The accepted historical migration/rollback rehearsal remains documented in [PostgreSQL catalogue migration and cutover](postgresql-catalogue-cutover.md). [Historical SQLite persistence operations](sqlite-persistence.md) is retained only so older delivery evidence remains understandable and link-valid.

## Readiness and specialized references

Do not call a planned feature available merely because an old pilot/runbook describes it. Formal lifecycle status lives in the work-item registry. Use the [operations index](index.md) to distinguish current runbooks from retained experiment/migration evidence.
