# WI-0212 — video metadata and manual people: verification

## Current delivery state

PR #765 merged the WI-0212 implementation and PR #766 merged its guarded
v38 -> v39 rehearsal gate. **Maintained-host v39 acceptance passed on
2026-11-08.** The maintainer confirmed a current streaming backup, isolated
full restore, guarded schema rehearsal, production v39 startup and functional
checks. This document retains the original migration procedure for recovery
and future reference; do not repeat the v38 rehearsal on an already migrated
v39 production database. Follow-up user-experience requirements are recorded
in the work item and need separate CI/local acceptance.

## Implemented boundary

WI-0151 already records capture time/provenance, make/model, GPS, coded
dimensions, rotation, duration and frame rate when present. WI-0212 reads
those PostgreSQL fields in Video Details; it does **not** add a second
FFprobe pipeline. Missing values display Unknown, not guessed timestamps
from filesystem modification time.

PostgreSQL v39 adds video-only append-only histories for manual capture-time,
canonical Place and known-person presence. Editing requires a current,
included video revision, and source exclusions purge the annotations.
Photo metadata, face evidence, recognition, saved collections and originals
are not modified.

- Manual person tagging means *this person appears in the video somewhere*,
  not a recognized face, timestamped person track or identity suggestion.
- Manual Places/clears take precedence over automatic GeoNames results.
- The explicit "Look up Place from GPS" control reuses the configured
  GeoNames provider and existing photo coordinate cache *reads*. Opening
  details never sends GPS or triggers hydration; video-only GPS is not
  newly added to a global cache. Provider pacing still applies.
- Recording time is displayed in UTC; manual edits require ISO 8601 with
  an explicit UTC designator or time-zone offset, e.g.
  2024-05-06T14:30:00+02:00.
- Retained proxies can still play normally; a missing proxy does not
  prevent catalogue metadata editing or initiate processing.

## Automated checks — development stage

On an updated checkout using Windows PowerShell 7.6:

~~~powershell
Set-Location "C:\Kod\codex\Photo Identity Indexer"
dotnet run --project tools/PhotoIdentity.Docs -- validate
if ($LASTEXITCODE -ne 0) { throw "Docs validation failed" }
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
if ($LASTEXITCODE -ne 0) { throw "Generated docs differ" }
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~VideoDetailsPostgresTests|FullyQualifiedName~VideoDetailsLayoutContractTests"
if ($LASTEXITCODE -ne 0) { throw "WI-0212 tests failed" }
~~~

The PostgreSQL test **does not actually execute** unless
PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING is configured.
Use a disposable PostgreSQL admin connection, never photoidentity_main.
The fixture creates and deletes its own temporary test database.
CI, documentation, disposable PostgreSQL checks and the maintained-host
restore/migration/real-video acceptance have all passed for the original v39
delivery. The follow-up presentation changes require a separate verification.

## Required v38 -> v39 restore rehearsal (PowerShell 7.6)

**Do not start a newly published v39 Photo Identity app against
`photoidentity_main` until this gate passes.** The new build automatically
initializes database schema during startup. Use an updated `main` containing
the v39 rehearsal command, and keep Photo Identity stopped throughout backup,
restore and rehearsal. Podman PostgreSQL must remain running. Confirm enough
Windows and Podman disk space for a full second database; the streaming
backup avoids staging a dump in Podman but the full restore does not.

```powershell
$ErrorActionPreference = "Stop"
Set-Location "C:\\Kod\\codex\\Photo Identity Indexer"
git status --short
git switch main
if ($LASTEXITCODE -ne 0) { throw "Could not switch to main." }
git pull --ff-only origin main
if ($LASTEXITCODE -ne 0) { throw "Could not update main." }

# Stop Photo Identity manually before this step; leave Podman PostgreSQL up.
.\\verify-postgres.ps1 -SkipContainerStart
if ($LASTEXITCODE -ne 0) { throw "PostgreSQL preflight failed." }

$databaseName = "photoidentity_main"
$backupPath = Join-Path $env:LOCALAPPDATA (
    "PhotoIdentity\\backups\\postgresql\\pre-wi0212-v39-" +
    (Get-Date -Format "yyyyMMdd-HHmmss") + ".dump"
)
.\\backup-postgres-catalogue-improved.ps1 `
    -DatabaseName $databaseName -OutputPath $backupPath
if ($LASTEXITCODE -ne 0) { throw "Streaming backup failed." }

# A decoded dump is not enough: restore and compare all source table counts.
.\\verify-postgres-backup-restore.ps1 `
    -BackupPath $backupPath -ApplicationStopped -RetainRestoredDatabase
if ($LASTEXITCODE -ne 0) { throw "Isolated restore verification failed." }

$restoreReportPath = "$backupPath.restore-verification.json"
$restoreReport = Get-Content -LiteralPath $restoreReportPath -Raw |
    ConvertFrom-Json
if (-not $restoreReport.passed -or
    [int]$restoreReport.restoredEvidence.schemaVersion -ne 38 -or
    $restoreReport.sourceDatabase -ne $databaseName -or
    $restoreReport.verificationDatabase -eq $databaseName -or
    $restoreReport.cleanupStatus -ne "retained") {
    throw "Not a verified retained v38 restore; do not migrate production."
}
Write-Host "Verified retained restore:" $restoreReport.verificationDatabase

# Obtain existing localhost credentials from the private launcher without
# showing or copying them into a file. The rehearsal command replaces the
# connection string's Database with the verified restore report's target.
$launcherCandidates = @(
    $env:PHOTOIDENTITY_LAUNCHER_CONFIG
    (Join-Path (Get-Location) "PhotoIdentity.launcher.json")
    (Join-Path $env:LOCALAPPDATA "PhotoIdentity\\launcher.json")
)
$launcherPath = @($launcherCandidates | Where-Object {
    $_ -and (Test-Path -LiteralPath $_ -PathType Leaf)
} | Select-Object -First 1)[0]
if (-not $launcherPath) { throw "Private launcher config not found." }
$launcher = Get-Content -LiteralPath $launcherPath -Raw | ConvertFrom-Json
$variable = [string]$launcher.postgresConnectionEnvironmentVariable
if (-not $variable) { throw "PostgreSQL launcher variable name missing." }
[string]$connection = ""
foreach ($scope in @(
    [EnvironmentVariableTarget]::Process,
    [EnvironmentVariableTarget]::User,
    [EnvironmentVariableTarget]::Machine
)) {
    $candidate = [Environment]::GetEnvironmentVariable($variable, $scope)
    if (-not [string]::IsNullOrWhiteSpace($candidate)) {
        $connection = $candidate
        break
    }
}
if (-not $connection) { throw "Configured local PostgreSQL credential missing." }
try {
    $env:PHOTOIDENTITY_WI0212_REHEARSAL = $connection
    dotnet run --project src/PhotoIdentity.Cli -c Release -- catalogue rehearse-v39 `
        --postgres-connection-env PHOTOIDENTITY_WI0212_REHEARSAL `
        --restore-report $restoreReportPath
    if ($LASTEXITCODE -ne 0) { throw "Isolated v38 -> v39 rehearsal failed." }
}
finally {
    Remove-Item Env:PHOTOIDENTITY_WI0212_REHEARSAL -ErrorAction SilentlyContinue
    $connection = ""
}
```

The rehearsal selects **only** the verified
`photoidentity_restore_YYYYMMDDHHMMSS_xxxxxx` database, rejects a
non-local server, rejects a source schema other than 38, confirms all
pre-migration row counts, applies schema initialization twice, and checks
the v39 migration record, empty capture-time/Place/person action histories,
and exclusion-purge trigger. It does not change production or read media.

Retain the restored database after the rehearsal until results are reviewed.
To inspect owned restored copies without modifying any database:

```powershell
.\\manage-postgres-restores.ps1
```

If you choose to retire **only this** verified copy, use its exact name and
ownership marker from `$restoreReport` (never a wildcard):

```powershell
.\\manage-postgres-restores.ps1 `
    -RemoveDatabase $restoreReport.verificationDatabase `
    -ExpectedMarker $restoreReport.restoreOwnershipMarker `
    -ProductionDatabaseName $databaseName -ConfirmRemoval
```

Do not run that cleanup when investigating any failure.

## Final maintained-PC acceptance — after development is complete

1. Confirm a current recoverable PostgreSQL backup, then rehearse v39
   upgrade and repeat initialization against an isolated restored DB.
   Do not open the new application against production before backup.
2. Launch the tested build with the existing approved Windows launcher
   and configured PostgreSQL connection.
3. Open a deliberately selected current video from Videos -> Video
   details & people. Inspect date **and time**, make/model, duration,
   rotation-correct resolution and GPS. Some files legitimately lack
   these fields; UI must say Unknown instead of fabricating metadata.
4. On a video with known embedded GPS and configured GeoNames, explicitly
   request Place lookup. Check canonical hierarchy and manual correction
   precedence. A non-GPS video must not contact the provider.
5. Edit recording time with an ISO time-zone offset; verify persistence
   after restart. Clear it and check that the extracted value is restored.
6. Add/remove an existing canonical person. Verify presence history is
   independent of face observations, review actions, suggestions and
   photo collections, including when no face was detected in video.
7. Open another *already catalogued* video whose original is online-only.
   Read/edit details without playback, processing or source access.
   Confirm the original stays online-only and unpinned.
8. Confirm ordinary photo Details/People/Places, photo search, video
   browsing/playback and Video Search regression checks pass.

Share sanitized PASS/FAIL only. Do not upload names, GPS coordinates,
filenames, original paths, PostgreSQL credentials or private frames.

## Maintained-host verification evidence (2026-11-08)

The maintainer reported **PASS** for git main, 10/10 focused automated
tests, documentation validation, pre-existing streaming backup SHA-256,
isolated restore, v38 -> v39 rehearsal, production v39 startup, video metadata,
recording-time edit/restore, explicit GeoNames lookup, manual Place edits,
manual person add/remove, online-only original retention, photo regression,
and video playback/search regression. Initial reuse of an older backup failed
exact row-count comparison after legitimate catalogue writes; a fresh backup
resolved this without changing or rolling back source records.

The real-video observations led to a follow-up draft PR for:
- Local-time recording presentation and minute-resolution local input.
- Simultaneous extracted/effective recording times when overridden.
- Friendly fps, orientation, codecs, bit depth and HDR details already in v39.
- Explicit restore of the last GPS-resolved Place after a manual override or
  manual clear (history-only, no new GeoNames call).

Still to be developed separately: vendor-specific Samsung model extraction
(where FFprobe exposes the tag), persisted audio channel/sample-rate fields,
and video bitrate (Mb/s). Existing metadata is revision-scoped; do not
silently re-inspect online-only originals. Bitrate depends on codec/content
and is not an objective picture-quality score.

### Follow-up focused local checks (after merging follow-up PR)

1. Rebuild and launch the updated application on already-migrated v39;
   **no** new database migration or backup rehearsal is needed.
2. In a browser with a non-UTC local zone, verify extracted UTC timestamps
   are presented as correct browser-local wall-clock times.
3. Enter a local `YYYY-MM-DD HH:MM` correction; save, refresh and confirm
   both corrected and extracted times are visible in local time. Restore
   extracted time; confirm manual-only details disappear.
4. Compare a rational frame rate against displayed fps, and confirm
   portrait/landscape and already-known video/audio codecs, bit depth, and
   HDR signalling. Unknown fields must remain Unknown.
5. With a clip already GPS-resolved, manually choose a different Place,
   then choose **Restore previous GPS Place**; confirm the earlier canonical
   result returns without GeoNames traffic. Repeat after **Clear Place**.
   Test a clip with no previous resolved Place: no restore option should show.
6. Reconfirm video details, person tagging and an online-only original do
   not read original bytes, and photo/video regressions remain green.

### PR #771 maintainer follow-up and date-editor correction (2026-11-09)

The maintainer confirmed **PASS** for updated checkout, focused tests,
documentation, production schema v39, startup, locally displayed recording
time, whole/decimal fps formatting, orientation, codecs, bit depth, HDR,
restoring the previous GPS Place after both manual edit and manual clear,
no unintended GeoNames calls, manual people, online-only preservation,
photo regressions and video playback/search.

**FAIL**: in a browser configured with a day-first locale, the native
`datetime-local` control displayed `DD/MM/YYYY HH:MM` while Blazor
converted the changed value and the handler parsed a strict
`yyyy-MM-ddTHH:mm` string. Save rejected a legitimate selected date;
original-versus-overridden display and restore therefore could not be
verified. The follow-up fix changes the editor to a **plain text input**
with a stable `YYYY-MM-DD HH:MM` format regardless of browser locale,
keeps browser-local timezone conversion to an offset-bearing API payload,
and retains strict daylight-saving overlap/gap rejection. The same fix
changes frame-rate formatting to the nearest whole fps.

After this fix merges, verify only the focused UI flow on the already
migrated v39 catalogue (do not repeat the v38 -> v39 restore/rehearsal):

1. Update `main`; run
   `dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~VideoDetailsPresentationTests|FullyQualifiedName~VideoDetailsLayoutContractTests|FullyQualifiedName~VideoDetailsPostgresTests"`.
2. Build using `./Package-PhotoIdentity.ps1 -Configuration Release` and
   relaunch the approved publish path with the private launcher.
3. In the locale where the old picker showed `24/02/2012 00:00`, enter
   `2012-02-24 00:00` into the plain textbox and save. It must work without
   a format error; inspect persisted local display after a page refresh.
4. Confirm both corrected and originally extracted times appear in local
   time; select **Use extracted time** and confirm the extracted time is
   restored and the original-only second row disappears.
5. Verify `496000/16577` is displayed as **30 fps**, not 29.92 fps.
6. Confirm GPS Place restoration, manual people and online-only original
   retention remain unaffected. No source inspection or schema changes.


Automatic background video geocoding, bulk rescans, collection membership
(WI-0153), video slideshow (WI-0154), video-face recognition (WI-0155)
and video-level descriptions/search revision (WI-0213) remain separate
future scope. The current manually triggered lookup should not be
confused with the photo background enrichment worker.
