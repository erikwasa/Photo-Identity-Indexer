# WI-0151 — consolidated maintained Windows acceptance

**Status: completed / maintainer-accepted on 2026-10-19.** This remains the reproducible operator procedure and is not a request to rerun ingestion, backup, or maintenance merely for work-item closeout. The supported initial poster policy is deliberately **Rec.709 (`bt709`) only**. HLG/PQ remain catalogued as video with technical metadata but without a durable poster. Private HLG previews are an **evaluation**, not production poster evidence. Never enable unreviewed HDR posters. A future versioned HDR profile can follow the visual review.

**Scope:** Verify opt-in local MOV/MP4 video ingestion and revision metadata, durable bounded SDR JPEG posters, default-off read-only video library, mixed-media isolation, safety of OneDrive placeholders and originals, and a sample of HLG preview quality. Video playback, clips in slideshows, general video search, video manual metadata, video analysis jobs and full-HDR production poster serving are **not** WI-0151 acceptance requirements (see WI-0152, WI-0153, WI-0154, WI-0188/0189/0190).

No private paths, credentials, original images/videos, GPS, camera labels, review JPEGs or unredacted `video-pilot-report.json` should be posted in issues or PRs. Share **counts, timings, generic outcomes and error text** only.

## A. Establish the normal baseline, before any video writes

From the repository root, after merging the last WI-0151 PR, run on the maintained Windows machine:

```powershell
git pull --ff-only
.\verify-postgres.ps1
.\Package-PhotoIdentity.ps1 -Configuration Release
$baseUrl = 'http://127.0.0.1:5080'  # substitute your existing launcher URL if different
Invoke-RestMethod "$baseUrl/health" | ConvertTo-Json -Depth 6
```

Require PostgreSQL ready, `schemaVersion = 33`, normal photo galleries/search/Smart and Creative Collections, slideshows and familiar archive totals. Record before/after counts for a few representative photo-only searches and collections. With `PhotoIdentity__VideoLibraryEnabled` absent or `false`, the video API must be unavailable (HTTP 404); the opt-in Videos page must not reveal content.

Do **not** repeat the v32→v33 migration: the maintainer already accepted the isolated rehearsal and healthy maintained schema v33. If health is not ready, resolve that before the video work.

## B. Resolve exact local inputs (no writes yet)

Substitute your REAL existing archive root and the verified **authoritative** PostgreSQL database. A historical rehearsal database name in an example is never a safe default.

```powershell
$sourceRoot = 'C:\YOUR_EXISTING_ARCHIVE_ROOT'
$databaseName = 'YOUR_VERIFIED_AUTHORITATIVE_DATABASE'
$derivatives = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\video-derivatives'
$ffprobe = (Get-Command ffprobe -ErrorAction Stop).Source
$ffmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source

$launcherCandidates = @(
    $env:PHOTOIDENTITY_LAUNCHER_CONFIG
    (Join-Path (Get-Location) 'PhotoIdentity.launcher.json')
    (Join-Path $env:LOCALAPPDATA 'PhotoIdentity\launcher.json')
)
$launcherPath = $launcherCandidates |
    Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
    Select-Object -First 1
if (-not $launcherPath) { throw 'No existing launcher configuration found.' }

$launcher = Get-Content -LiteralPath $launcherPath -Raw | ConvertFrom-Json
$connectionVariableName = [string]$launcher.postgresConnectionEnvironmentVariable
if ($connectionVariableName -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
    throw 'Launcher PostgreSQL variable reference is absent or invalid.'
}
if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
    throw 'Archive source does not exist.'
}
if ([IO.Path]::GetFullPath($sourceRoot).TrimEnd('\') -eq
    [IO.Path]::GetFullPath($derivatives).TrimEnd('\')) {
    throw 'Derivative root must not equal the source.'
}
New-Item -ItemType Directory -Path $derivatives -Force | Out-Null

# CLI commands resolve only process environment; copy the configured launcher
# credentials WITHOUT PRINTING them if they currently exist in User/Machine scope.
if (-not [Environment]::GetEnvironmentVariable($connectionVariableName, 'Process')) {
    $secret = [Environment]::GetEnvironmentVariable($connectionVariableName, 'User')
    if (-not $secret) {
        $secret = [Environment]::GetEnvironmentVariable($connectionVariableName, 'Machine')
    }
    if (-not $secret) { throw 'Configured PostgreSQL credentials variable is not set.' }
    [Environment]::SetEnvironmentVariable($connectionVariableName, $secret, 'Process')
    Remove-Variable secret
}
& $ffprobe -version | Select-Object -First 1
& $ffmpeg -version | Select-Object -First 1
```

Confirm `$derivatives` is an independent, non-junction local directory outside OneDrive originals, and that your actual maintained API uses this **same** poster root for exclusion/purge. The poster root must remain stable through any exclusion/purge in progress.

Select a small set of already-local source MOV/MP4 files (prefer a mixture of H.264 SDR, HEVC SDR, HEVC HLG, rotated iPhone footage, and unsupported/unknown cases). **Do not make an entire OneDrive folder always-available.** The CLI's `--max-videos` limit samples the first locally available candidates during archive enumeration, not an arbitrary selected list; check its summary and, if necessary, do not claim a particular class was tested.

## C. Dry runs and verified backup

Dry-run video ingestion (no PostgreSQL writes, but reads/probes bounded local video bytes):

```powershell
dotnet run --project src/PhotoIdentity.Cli -c Release -- video ingest --enable-video --source $sourceRoot --ffprobe $ffprobe --max-videos 8 --timeout-seconds 30
if ($LASTEXITCODE -ne 0) { throw 'Video ingest preview failed.' }
```

Require zero unexpected failures/changed files and no OneDrive placeholder hydration. Record the aggregate `local`, `placeholders`, `supported`, `unsupported`, `attempted` counts. Before any `--apply`, take a **new backup of the verified authoritative database** using the repository's maintained PostgreSQL backup/restore verification procedure. Preserve the private backup and successful verification report; do not assume `--apply` selects a database from the launcher automatically.

If no representative SDR file is within the bounded candidate set, the subsequent poster step may validly produce zero posters. Do not treat zero posters as visual acceptance.

## D. Opt-in video writes (only after backup)

All commands below name the explicit database and source selected in step B. The source must match the already registered local-folder archive source.

```powershell
dotnet run --project src/PhotoIdentity.Cli -c Release -- video ingest --enable-video --apply --postgres-connection-env $connectionVariableName --database $databaseName --source $sourceRoot --ffprobe $ffprobe --max-videos 8 --timeout-seconds 30
if ($LASTEXITCODE -ne 0) { throw 'Bounded video ingest failed.' }

dotnet run --project src/PhotoIdentity.Cli -c Release -- video posters --enable-video --source $sourceRoot --derivatives $derivatives --ffmpeg $ffmpeg --postgres-connection-env $connectionVariableName --database $databaseName --max-videos 8 --timeout-seconds 30
if ($LASTEXITCODE -ne 0) { throw 'Poster preview failed.' }

dotnet run --project src/PhotoIdentity.Cli -c Release -- video posters --enable-video --apply --source $sourceRoot --derivatives $derivatives --ffmpeg $ffmpeg --postgres-connection-env $connectionVariableName --database $databaseName --max-videos 8 --timeout-seconds 30
if ($LASTEXITCODE -ne 0) { throw 'Bounded poster publication failed.' }
```

Only `bt709` supported current revisions with verified unchanged local bytes are poster-eligible. HLG (`arib-std-b67`), PQ (`smpte2084`) and unknown transfer must remain deferred without a misleading SDR JPEG. Repeat these bounded commands once on unchanged files: no new revision or conflicting poster should be introduced; retries must be idempotent. If you see unexpected `failures`, `changed`, `newRevisions`, or unsupported results, stop and report the **aggregate** summary before increasing `--max-videos`.

## E. Enable the read-only Videos page using the maintained launcher

The final launcher checkpoint permits an explicit `PhotoIdentity__VideoLibraryEnabled` setting and **requires** an existing, absolute, non-junction `PhotoIdentity__VideoPosterRoot`. Do not set the video flag without verifying this is the same app-owned root used by the poster generator and source-copy purge.

1. Stop the existing Photo Identity API using your normal Windows application stop procedure; launching again while it is already running does **not** reload settings.
2. Make a private backup of the existing launcher JSON before editing. In its `settings` object add these two entries (retain the existing settings and PostgreSQL *variable reference*, not credentials):

```json
"PhotoIdentity__VideoLibraryEnabled": "true",
"PhotoIdentity__VideoPosterRoot": "C:\\YOUR_APPROVED_LOCAL_DERIVATIVE_ROOT"
```

3. Substitute the **actual** value of `$derivatives` as a JSON-escaped absolute path. From the repo root, validate the configuration without starting a server:

```powershell
.\Start-PhotoIdentity.ps1 -ConfigurationPath $launcherPath -ValidateConfigurationOnly
```

4. Restart Photo Identity normally from the launcher, then check `$baseUrl/health`, `$baseUrl/api/videos?offset=0&limit=24`, and Advanced → Archive operations → Videos (opt-in). Supported current videos should have correct duration, orientation/dimensions and, when published, a working JPEG. Unsupported/HLG/unknown videos may appear without posters and must have no invented metadata. Offline/online-only originals must **not** be opened merely to serve an existing JPEG.
5. Check returned video JSON for absence of private source roots, filenames, camera make/model and GPS. Do not upload the JSON if it contains anything sensitive.

Recheck normal photo gallery counts, both semantic-search modes, Smart/Creative Collections, photo person/tag/place/capture-date editing, the archive processing queue and slideshow creation/playback. Video revisions must not enter photo results, processing or slideshows. Do not enable any video worker or playback feature.

## F. Privacy, revision safety and cleanup

- Test malformed/nonexistent video revision poster URLs: HTTP 404, never a path or server stack trace.
- Review the test results for source-copy exclusion/purge, poster publication serialization, superseded revisions and JPEG checksum corruption from the merged PostgreSQL CI suite. **Do not exclude or delete a real irreplaceable video just to reproduce those destructive cases.** If desired, rehearse these scenarios separately on disposable synthetic test sources.
- Inspect the bounded cleanup plan **without** `--apply`:

```powershell
dotnet run --project src/PhotoIdentity.Cli -c Release -- video posters-cleanup --enable-video --source $sourceRoot --derivatives $derivatives --postgres-connection-env $connectionVariableName --database $databaseName --max-files 3 --min-age-hours 24
```

The fresh posters should remain protected by the minimum 24-hour age and current-revision checks. Do not perform a deletion during initial acceptance. Confirm the configured video poster root remains unchanged whenever source-copy exclusion/purge may run.

## G. Optional private HLG visual evaluation

Run only after all mandatory SDR checks pass. **This writes private pilot derivatives, not catalogue posters**:

```powershell
$hlgReview = Join-Path $env:LOCALAPPDATA 'PhotoIdentity\private-hlg-review'
New-Item -ItemType Directory -Path $hlgReview -Force | Out-Null
dotnet run --project src/PhotoIdentity.Cli -c Release -- video pilot --enable-video --preview-hlg --source $sourceRoot --derivatives $hlgReview --ffprobe $ffprobe --ffmpeg $ffmpeg --max-videos 25 --timeout-seconds 30
```

Check private JPEG vs source in a colour-managed viewer for correct rotation, skin colours, shadows, highlight detail, saturation and clipping. Record only anonymous aggregate observations. **Do not** enable HLG/PQ in the production library as a result of this pilot; a separately versioned, lifecycle-complete profile needs a later deliberate code change. Missing explicit BT.2020/HLG stream colour evidence is always deferred.

## H. Exit and report

Return the maintained app to the expected posture after checking the gallery: set `PhotoIdentity__VideoLibraryEnabled` back to `"false"` in the launcher (or restore the backed-up JSON), stop and restart normally; confirm `/api/videos` is disabled. Keep any explicitly ingested video rows for review unless you have a separately approved cleanup/rollback plan; restoring the launcher file does **not** delete database records or private pilot files.

**Pass / fail summary to share:** Release build and `verify-postgres.ps1`; health/schema v33; DB-backup verification; aggregate ingest and poster counts; example supported SDR orientation and poster appearance; HLG/unknown poster deferral; correct capture-date/timezone/rotation and unfilled GPS/camera fields where absent; unchanged photo gallery/search/collection/slideshow counts; video-only page behaviour and disabled-state behaviour; no unexpected hydration; originals unchanged; and any generic error messages. WI-0151 was completed after the maintainer reviewed and accepted the evidence on 2026-10-19; retain this checklist for future controlled testing.

## I. Accepted maintained Windows results — 2026-10-19

**Decision: WI-0151 completed.** The maintainer explicitly marked all **11/11** local acceptance checklist items PASS after confirming WI-0202's photo-query regression was resolved:

| Gate | Maintainer-reported outcome |
| --- | --- |
| PostgreSQL verification, Release package and schema v33 | PASS |
| Video API disabled by default (404) | PASS |
| Opt-in video-ingest dry run | PASS |
| Actual authoritative PostgreSQL backup and isolated restore | PASS |
| Bounded video-ingest apply and technical metadata persistence | PASS |
| Durable SDR still poster, visual inspection and idempotent retry | PASS |
| Explicitly enabled read-only video library and poster/metadata display | PASS |
| Photo Archive/search/Smart+Creative Collections/slideshows unchanged | PASS |
| No unwanted OneDrive hydration or original-file change | PASS |
| Nonexistent poster 404 and dry-run-only poster cleanup | PASS |
| Disabled video API (404) after restoring launch configuration | PASS |

**Final reported materialization counters:** Enumerated **1,316**, eligible **1**, existing **1**, new posters **0**, HDR deferred **7**, skipped **1,308**, changed **0**, failures **0**. One durable SDR poster had already been created and visually inspected as acceptable. The last run's `new posters = 0` is a correct idempotent retry, not a claim that no poster exists. These are aggregate results for the bounded local run and do not imply that all enumerated videos were read.

Poster reads were reported to remain revision-bound and exclusion-aware; no original videos, photo analysis or cloud hydration were modified. No destructive exclusion or physical deletion was carried out on irreplaceable media. HLG/PQ and unknown-transfer production posters remain deferred by design: **initial Rec.709 SDR acceptance does not approve HDR tone mapping or HDR serving**. The optional private HLG comparison is separate research, not a missing gate for the initial video slice.

Playback, worker scheduling, searchable video captions/embeddings, video manual metadata and slideshow video clips remain separate M30 work. This acceptance records a real-file SDR sample, not universal codec coverage, production HDR quality approval or mass-video throughput.
