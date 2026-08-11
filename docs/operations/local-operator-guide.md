# Local operator guide

This is the authoritative local operating path for Photo Identity Indexer on Windows. It distinguishes the current permanent-archive workflow from retained pilot/evaluation tooling.

Check [current delivery status](../delivery/status/current.md) before treating a planned feature as available. In particular, version 1 is not declared archive-ready until the permanent-ingestion, bounded-storage and archive-format gates are complete.

## Trust and privacy boundary

- Keep personal photos, catalogues, crops, embeddings, proxies and private reports outside the repository.
- Keep the SQLite catalogue on a local disk, not a network share or synchronised cloud folder.
- Treat the Windows computer as the trusted control plane.
- Original photos are read-only inputs.
- The browser application is unauthenticated. Prefer localhost; use another device only on a trusted private network with narrow firewall scope.
- Personal OneDrive is accessed through the Windows sync client, not Microsoft Graph.
- Azure is optional and never receives OneDrive credentials or the canonical review database.

## 1. Build and verify the application

From the repository root:

```powershell
./build.ps1
./test.ps1
./verify-local.ps1 -InstallModels
./models/install-models.ps1 -Id centerface-2019-fp32
./verify-review.ps1 -Mode Smoke -Configuration Release
```

The permanent archive analysis profile is governed separately from the legacy generic `batch` defaults. Permanent archive analysis uses:

- detector `centerface-2019-fp32`;
- confidence `0.5`;
- detector pipeline `single-pass`;
- embedder `sface-2021dec-fp32`; and
- SFace five-point alignment with the governed archive configuration.

Do not infer the production archive detector from the generic `batch start` default, which remains a legacy/general-purpose interface.

## 2. Choose permanent local paths

Use local non-OneDrive paths for the canonical database, model/analysis output and review proxies. Keep those paths separate from the authoritative photo archive.

Example layout:

```powershell
$root = "C:\PhotoIdentity"
$db = Join-Path $root "catalogue.db"
$analysis = Join-Path $root "analysis"
$proxies = Join-Path $root "review-proxies"
$publish = Join-Path $root "app"
$backups = Join-Path $root "backups"

New-Item -ItemType Directory -Force -Path $root,$analysis,$proxies,$backups | Out-Null
```

The actual Personal OneDrive archive root is private configuration and must not be committed to Git.

## 3. Configure bounded archive storage

The API host reads the catalogue and archive settings before normal operation. At minimum, permanent archive operation needs the database, analysis output and selected review-proxy configuration. Managed hydration remains disabled until explicit limits are supplied.

PowerShell environment-variable form:

```powershell
$env:PhotoIdentity__DatabasePath = $db
$env:PhotoIdentity__ArchiveAnalysisOutputRoot = $analysis
$env:PhotoIdentity__ReviewProxyRoot = $proxies
$env:PhotoIdentity__ReviewProxyProfileId = "<accepted-profile-id>"
$env:PhotoIdentity__ReviewProxyMaximumLongEdge = "<accepted-max-edge>"
$env:PhotoIdentity__ReviewProxyJpegQuality = "<accepted-jpeg-quality>"
$env:PhotoIdentity__ArchiveHydration__MinimumFreeSpaceReserveBytes = "<accepted-reserve>"
$env:PhotoIdentity__ArchiveHydration__MaximumManagedHydrationBytes = "<accepted-budget>"
$env:PhotoIdentity__ArchiveHydration__MaximumConcurrentOperations = "<accepted-concurrency>"
```

Do not invent production values. Use the values accepted through [bounded archive acceptance](bounded-archive-acceptance.md). See [review-proxy serving and bounded originals](review-proxy-serving.md) for exact semantics.

For routine packaged use, store the same accepted values in `%LOCALAPPDATA%\PhotoIdentity\launcher.json` instead of setting them manually before every start. Copy the packaged `PhotoIdentity.launcher.example.json` there and add only the accepted settings required by the installation. For the packaged application, normally leave `publishPath` unset: the package entry point selects the code directory, while private configuration remains durable outside the replaceable package. The real launcher configuration must remain private.

## 4. Install and run the Windows application

Normal M18 operation uses the self-contained `win-x64` operator package. A separately installed .NET runtime and manual `dotnet publish` are not required for routine use.

To build the package from a repository checkout:

```powershell
./Package-PhotoIdentity.ps1 -Configuration Release
```

The default ZIP is:

```text
.artifacts\packages\PhotoIdentity-win-x64.zip
```

Extract the complete ZIP to a local code-only folder such as `C:\Apps\PhotoIdentity-<version>`, then start Photo Identity by double-clicking:

```text
PhotoIdentity.cmd
```

The packaged entry point delegates to the WI-0051 launcher and therefore:

- accepts only a loopback HTTP URL such as `http://127.0.0.1:5080`;
- opens the browser only after `/health` reports the application ready;
- reuses an already healthy Photo Identity instance instead of starting a duplicate;
- refuses to start when the configured port is occupied by something that is not the Photo Identity health endpoint;
- loads only the documented `PhotoIdentity__...` bootstrap settings from the private JSON file; and
- writes startup stdout/stderr logs under `%LOCALAPPDATA%\PhotoIdentity\launcher-logs` when troubleshooting is needed.

The package directory contains replaceable application code only. Keep the catalogue, analysis output, proxies, launcher configuration and backups outside it. Upgrade by extracting a new package beside the old one, stopping the old `PhotoIdentity.Api.exe`, starting `PhotoIdentity.cmd` from the new folder, verifying the existing catalogue/settings, and only then deleting the old package folder. See [Windows operator package](windows-package.md) for the complete package, deployment trade-off and verification procedure.

Manual framework-dependent publishing remains available for development and diagnostics:

```powershell
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish `
  .\src\PhotoIdentity.Api\PhotoIdentity.Api.csproj `
  --configuration Release `
  --output $publish
```

The repository `Start-PhotoIdentity.cmd` launcher can start that prepared publish when its private launcher configuration supplies the matching `publishPath`. Direct command-line startup also remains available for diagnostics:

```powershell
Push-Location $publish
dotnet .\PhotoIdentity.Api.dll --urls "http://127.0.0.1:5080"
```

Open `http://localhost:5080` on Windows when starting manually.

## 5. Configure permanent archive coverage

Use the **Archive** page for normal operation. The same core coverage operations are also available through the CLI.

First inclusion:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  archive include `
  --database $db `
  --root "<private-archive-root>" `
  --folder "<relative-folder>"
```

Later inclusions use the same permanent root and another relative folder. A broader parent may be added later; normalized parent coverage subsumes previously listed children rather than creating another source identity.

Synchronize coverage:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  archive sync --database $db

dotnet run --project src/PhotoIdentity.Cli -- `
  archive status --database $db
```

Synchronization revisits all included coverage for new, changed, missing and newly available files. It must not repeat unchanged exact-profile work.

## 6. Advance the archive

Use **Advance archive** in the Archive page for the bounded permanent workflow. It coordinates source verification, managed OneDrive hydration, governed analysis, durable proxy generation and release/retry behavior.

Important distinctions:

- OneDrive availability and source verification are separate states.
- Metadata changes can require verification but never establish an immutable revision by themselves.
- Authoritative SHA-256 bytes establish or reselect revisions.
- First-time online-only sources may need temporary bounded hydration before their first revision exists.
- Ordinary collection browsing uses proxies and must not hydrate originals.
- Explicit original viewing uses the separate hydrate/status/view/release lifecycle.

The CLI `archive analyze` command exists for the archive analysis coordinator, but it is not a substitute for the complete bounded online-only advancement path when source verification or managed hydration is required.

## 7. Media-format completeness

HEIC/HEIF is being added under WI-0053; RAW support is activated only for formats actually found in the real archive. Use the aggregate inventory before treating a coverage area as format-complete:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  archive inventory --database $db
```

The inventory walks only configured archive coverage, does not open image content, and prints extension counts plus media family/current support state. It does not print source paths or filenames. Because it only enumerates directory entries, it can reveal online-only HEIC or future RAW extensions without requesting OneDrive hydration.

Expected examples include:

```text
extension: .heic count=<n> family=heif supported=true
extension: .dng count=<n> family=raw supported=false
```

A RAW line with `supported=false` is a deliberate trigger for format-specific WI-0053 work, not permission to omit the file. When the current archive reports no RAW family, retain that aggregate result and defer RAW decoding until a real variant appears.

Do not treat a scan with silently omitted media as full archive coverage.

## 8. Review faces and maintain people

The current runtime supports canonical manual assignment, rejection, undo, person creation/maintenance and exact-model identity suggestions.

Use the browser application to review new faces and maintain people. Rejected face-person pairs remain durable negative evidence.

The accepted future direction under ADR-0006/WI-0043 adds configurable confidence groups and optional canonical High-confidence automatic assignment. **That policy must not be documented as operationally available until WI-0043 is implemented.**

Unknown-as-a-review-state is similarly planned under WI-0047; until then, do not create a synthetic person named `Unknown` as a workaround.

## 9. Regenerate current identity suggestions

Until WI-0045 moves regeneration into the browser, use the CLI against one exact embedder revision:

```powershell
$embedder = Get-Content `
  .\models\manifests\sface-2021dec-fp32.json -Raw | ConvertFrom-Json

dotnet run --project src/PhotoIdentity.Cli -- `
  match regenerate `
  --database $db `
  --embedder-id $embedder.modelId `
  --embedder-hash $embedder.sha256
```

Current regeneration is advisory and does not itself create automatic canonical assignments. WI-0043 changes that behavior only when its explicit automatic policy is implemented and enabled.

## 10. Browse collections and originals

Use `/collections` for people-based collection queries. Normal thumbnails/previews are served from durable review proxies when configured.

Request authoritative full-resolution content only through the explicit original lifecycle. The API verifies the expected immutable revision before serving it and tracks only Photo-Identity-owned hydration for later release.

See [review-proxy serving and bounded originals](review-proxy-serving.md) for the detailed API semantics.

## 11. Back up and restore

Treat the SQLite catalogue and governed local artefacts as sensitive permanent data.

Before a maintenance copy:

1. stop the API, CLI and workers;
2. confirm no writer has the database open;
3. copy the database and matching governed artefact directories in the same maintenance window; and
4. verify the copy with `PRAGMA integrity_check`, `PRAGMA foreign_key_check` and `PRAGMA user_version`.

Follow [SQLite persistence operations](sqlite-persistence.md) for the complete policy.

## Version-1 readiness check

Do not call the permanent archive ready merely because a small pilot works. The version-1 start gate requires:

- accepted bounded-storage/proxy behavior (WI-0042);
- completed permanent incremental ingestion behavior (WI-0041);
- completed HEIC/HEIF and real-archive RAW support (WI-0053); and
- the product [success criteria](../product/success-criteria.md) to be satisfied on the real Windows/OneDrive environment.

Once those gates pass, begin adding real archive coverage to the permanent catalogue and keep expanding it incrementally rather than creating a replacement database.

## Specialized references

Use the [operations index](index.md) to distinguish current runbooks from retained experiment evidence.