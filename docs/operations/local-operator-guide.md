# Local operator guide

This is the authoritative start-to-finish path for running Photo Identity Indexer on Windows. It covers the accepted local workflow; linked documents provide deeper subsystem detail.

## Trust and privacy boundary

- Keep personal photos, catalogues, crops, embeddings and reports outside the repository.
- Keep the SQLite catalogue on a local disk, not a network share or synchronised cloud folder.
- Treat the Windows computer as the trusted control plane.
- The browser application is unauthenticated. Use localhost or a trusted private network only.
- Original photos are read-only inputs and must not be modified.
- Azure is optional. It receives explicit portable bundles only and never receives OneDrive credentials, people or human review history.

## 1. Prepare Windows

Install:

- .NET 10 SDK;
- PowerShell 7 or Windows PowerShell;
- Git; and
- enough local disk space for the catalogue, aligned crops, embeddings, publish output and reports.

From the repository root, verify the source tree and install the pinned baseline models:

```powershell
./build.ps1
./test.ps1
./verify-local.ps1 -InstallModels
./verify-review.ps1 -Mode Smoke -Configuration Release
```

Expected success signals:

- the solution builds in Release configuration;
- automated tests pass;
- model files match their pinned SHA-256 manifests;
- living-document validation passes; and
- the disposable hosted review-application smoke test passes.

When model installation must be run separately:

```powershell
./models/install-models.ps1
```

The baseline model IDs are:

- detector: `yunet-2023mar-fp32`;
- embedder: `sface-2021dec-fp32`.

Model files are local dependencies and must not be committed.

## 2. Create an isolated workspace

Keep source photos and generated output in separate directories so derived files cannot be scanned as new inputs.

```powershell
$root = "C:\PhotoIdentityPilot"
$source = Join-Path $root "source"
$output = Join-Path $root "outputs"
$db = Join-Path $root "catalogue.db"
$publish = Join-Path $root "review-app"
$evaluation = Join-Path $root "model-lab"
$backup = Join-Path $root "backups"

New-Item -ItemType Directory -Force `
  -Path $source,$output,$evaluation,$backup | Out-Null
```

Stage a representative private set in `$source`. The accepted pilot used approximately 450–550 images, but a smaller disposable set is suitable for initial verification.

For Personal OneDrive, use the Windows sync client and point `$source` at a locally available synchronised folder. Do not add Microsoft Graph credentials.

## 3. Process the baseline catalogue

Start a resumable batch using explicit model IDs:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  batch start `
  --database $db `
  --source $source `
  --output $output `
  --detector-model yunet-2023mar-fp32 `
  --embedder-model sface-2021dec-fp32
```

Record the printed run ID:

```powershell
$runId = "REPLACE_WITH_RUN_ID"
```

Inspect progress:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  batch status --database $db --run $runId
```

Resume interrupted or incomplete work:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  batch resume --database $db --run $runId
```

Resume uses the model IDs persisted with the run. Do not attempt to change models during resume.

Expected success signals:

- the start command prints a run ID and selected model IDs;
- status reports total, pending, running, completed and failed work;
- resume continues the saved run rather than creating duplicate canonical revisions; and
- unsupported or unavailable media are reported without modifying the source.

## 4. Publish and run the local browser application

Publish a clean Release build:

```powershell
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish `
  .\src\PhotoIdentity.Api\PhotoIdentity.Api.csproj `
  --configuration Release `
  --output $publish
```

Start the API and hosted Blazor application:

```powershell
$env:PhotoIdentity__DatabasePath = $db
Push-Location $publish
dotnet .\PhotoIdentity.Api.dll --urls "http://0.0.0.0:5080"
```

Open `http://localhost:5080` on Windows.

For Pixel testing, use the Windows computer's LAN address on the same trusted private network. Restrict any Windows Firewall rule to the intended private profile. Clear old site data or unregister the service worker after publishing a materially changed web build.

## 5. Review faces and maintain people

Use the browser application to:

1. inspect unreviewed faces;
2. create or select a person;
3. assign, reject or undo individual decisions;
4. accept or reject ranked suggestions;
5. use preview-first bulk actions only after checking the group;
6. rename or merge people when needed; and
7. inspect progress by review state and exact model revision.

Human-confirmed assignments are canonical. Suggestions are advisory and must never become labels automatically.

Expected success signals:

- review actions are visible after refresh;
- undo reverses the selected action without deleting audit history;
- merged people resolve to the surviving person;
- assigned and rejected faces do not re-enter the unreviewed queue; and
- progress totals remain stable while paging.

## 6. Regenerate exact-model suggestions

Read the exact pinned embedder revision:

```powershell
$embedder = Get-Content `
  .\models\manifests\sface-2021dec-fp32.json -Raw | ConvertFrom-Json
```

Regenerate suggestions:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  match regenerate `
  --database $db `
  --embedder-id $embedder.modelId `
  --embedder-hash $embedder.sha256
```

Expected success signals:

- the command prints the exact model ID and hash;
- target and suggestion counts are reported;
- confirmed assignments and review history remain unchanged; and
- a previously rejected face-person pair does not reappear after regeneration.

## 7. Export and evaluate the reviewed catalogue

Read the pinned detector revision and define output paths:

```powershell
$detector = Get-Content `
  .\models\manifests\yunet-2023mar-fp32.json -Raw | ConvertFrom-Json

$manifest = Join-Path $evaluation "baseline.json"
$report = Join-Path $evaluation "baseline-report.json"
```

Export a deterministic reviewed-catalogue split:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  evaluate export `
  --database $db `
  --output $manifest `
  --dataset-id private-baseline-v1 `
  --pipeline-version local-pipeline-v1 `
  --detector-id $detector.modelId `
  --detector-hash $detector.sha256 `
  --embedder-id $embedder.modelId `
  --embedder-hash $embedder.sha256 `
  --seed private-baseline-split-v1 `
  --run $runId
```

Run evaluation:

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  evaluate `
  --dataset $manifest `
  --output $report
```

Expected success signals:

- export reports the exact detector and embedder revisions;
- validation chooses thresholds and the held-out test split only reports final metrics;
- repeating export and evaluation with unchanged inputs produces identical file hashes; and
- no private manifest or report is committed.

For a second model revision and same-corpus comparison, follow the [local evaluation and multi-model workflow](local-evaluation.md).

## 8. Browse collections

Open `/collections` in the browser application.

- Select one or more people.
- Choose **All selected people** or **Any selected person**.
- Keep **Confirmed assignments only** as the safe default.
- Enable advisory evidence only with an exact model revision and threshold.
- Inspect representative results and use pagination when needed.

The grid uses fixed 480 × 320 server-generated JPEG thumbnails. Source paths and filenames remain on the Windows host.

## 9. Request a neutral collection manifest

List people from the local API:

```powershell
$baseUrl = "http://localhost:5080"
$people = Invoke-RestMethod "$baseUrl/api/review/people"
$people | Select-Object id, displayName
```

Request a confirmed-only manifest for one person:

```powershell
$personId = $people[0].id
$manifest = Invoke-RestMethod `
  "$baseUrl/api/collections/manifest?people=$personId&match=all&reviewState=assigned"

$manifest.format
$manifest.version
$manifest.total
$manifest.photos | Select-Object -First 3 revisionId, thumbnailUrl, contentUrl
```

For two people:

```powershell
$personIds = "$($people[0].id),$($people[1].id)"
$anyManifest = Invoke-RestMethod `
  "$baseUrl/api/collections/manifest?people=$personIds&match=any&reviewState=assigned"
$allManifest = Invoke-RestMethod `
  "$baseUrl/api/collections/manifest?people=$personIds&match=all&reviewState=assigned"
```

Expected success signals:

- `format` is `photoidentity.collection-manifest`;
- `version` is `1`;
- URLs use opaque revision identifiers rather than filesystem paths;
- `any` totals are greater than or equal to corresponding `all` totals; and
- the response contains no source root, source key, crop path or filename.

## 10. Back up the catalogue

Use a quiesced file copy only:

1. stop the CLI, browser host and all workers;
2. confirm no process has the database open;
3. copy the SQLite file to a versioned backup path;
4. back up referenced crop and artefact directories in the same maintenance window; and
5. verify the database copy with `PRAGMA integrity_check`, `PRAGMA foreign_key_check` and `PRAGMA user_version`.

Do not copy an actively written database. See [SQLite persistence operations](sqlite-persistence.md) for the supported backup, restore, migration and locking policy.

Example stopped-state copy:

```powershell
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
Copy-Item $db (Join-Path $backup "catalogue-$stamp.db")
```

Backups contain biometric and identity data. Encrypt them and restrict access.

## 11. Clean up temporary artefacts

After verifying backups and retaining required private evidence:

```powershell
Pop-Location -ErrorAction SilentlyContinue
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
```

Remove obsolete transfer bundles, disposable evaluation outputs and redundant candidate artefacts. Keep the canonical catalogue and any required crop store together. Removing a model file prevents future inference with that revision but does not erase persisted canonical review history.

## Common recovery paths

### A model is unavailable or fails hash verification

Re-run the pinned installer and do not substitute an unrecorded binary:

```powershell
./models/install-models.ps1
./verify-local.ps1
```

### A run was interrupted

Use `batch status` and `batch resume` with the original run ID. Do not start a replacement run merely because the terminal closed.

### The browser still shows an older layout

Stop the host, publish again, clear site data or unregister the service worker, and reload from the newly published host.

### SQLite reports locking

Stop unnecessary writers and retry after the active short transaction completes. Do not move the database to a network share and do not use unbounded retry loops.

### Images or crops are missing

Confirm the source is locally available, the source root has not moved, and the crop/output directories referenced by the catalogue still exist. Restore the catalogue and artefacts from the same maintenance-window backup when recovery is required.

## Deeper references

- [Local evaluation and multi-model workflow](local-evaluation.md)
- [SQLite persistence operations](sqlite-persistence.md)
- [Architecture overview](../architecture/overview.md)
- [Canonical data model](../architecture/data-model.md)
- [Identity matching](../architecture/identity-matching.md)
- [Portable processing bundles](../architecture/portable-bundles.md)
- [Security and privacy](../architecture/security-and-privacy.md)
- [Model governance](../models/model-governance.md)
