# WI-0210 — live attribute-only check for Rec.709 full-range candidates

**Checkpoint 5 (proposed).** This is a metadata-only follow-up to the
accepted guarded synthetic conversion, **not** a production-video
operation. The confirmed production catalogue is `photoidentity_main`;
the container seed database `photoidentity` is NOT that catalogue.

## Purpose and exact limitations

The previous read-only PostgreSQL survey of 58 current video revisions
identified **12** Rec.709 `yuvj420p` video revisions blocked **only**
at the worker pixel-format metadata gate. It recorded `unknown` last
availability for all 58. The maintainer's checkpoint-4 Windows
synthetic acceptance **PASS** proved two H.264/AAC outputs:
landscape 720×406 with audio, portrait 406×720 without audio,
both with Rec.709 full-to-limited range and mandatory pinned FFprobe
validation. Scratch cleanup PASS; no database or original was opened.

`video compatibility local-check` obtains only those current,
nonexcluded, `local-folder` video revisions in a PostgreSQL
repeatable-read, read-only transaction, using the **identical**
worker metadata gate as the existing survey after substituting
`yuv420p` for the `yuvj420p` check. It reads the *private*
registered source root/key **inside the process** solely for a
bounded, Windows-only attributes/size test.

- Rejects root escapes, unsafe source paths, directory junctions and
  unsupported extensions before testing source availability.
- Reuses the existing OneDrive video **attribute-only**
  availability detector. Only files indicated as local undergo an
  additional length and leaf-reparse check; all content remains
  unopened. Online-only placeholders are never opened, hydrated,
  pinned, downloaded, hashed or probed.
- Outputs only grouped counts of `local-metadata-match`,
  `online-only`, `downloading`, `unavailable`, `size-mismatch`,
  `unsafe-source` and `error`. Does **not** output filenames,
  source keys, paths, UUIDs, content digests or private metadata.
- Caps candidate checks at **64**, rejecting larger sets rather than
  reporting a misleading partial tally.
- Reads PostgreSQL without migrations or changes. Does not access
  video stream content, FFmpeg, video workers, job queues,
  derivative files or Azure resources.
- A `local-metadata-match` count **is only current attributes and
  size**, not a SHA-256, revision-stability or safe-decoding proof.
  Files On-Demand state can change after the check. A separately
  approved source-integrity/read trial is required before processing.

## Verification on the maintained Windows host — AFTER PR merge

No local documentation validation or generation is necessary; CI runs
those checks. Use the checked-out **main** branch and confirmed
production database:

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main

$db = "photoidentity_main"
$envName = "PHOTOIDENTITY_POSTGRES_CONNECTION_STRING"
if ([string]::IsNullOrWhiteSpace(
    [Environment]::GetEnvironmentVariable($envName))) {
    throw "Existing production PostgreSQL environment variable is missing."
}

$lines = & dotnet run --project src/PhotoIdentity.Cli --configuration Release -- video compatibility local-check --postgres-connection-env $envName --database $db
if ($LASTEXITCODE -ne 0) {
    throw "Read-only WI-0210 live availability check failed."
}

$report = ($lines -join "`n") | ConvertFrom-Json -ErrorAction Stop
$report | Select-Object candidateClass, candidateCount, readOnly,
    filesystemMetadataChecked, localIsNotIntegrityProven,
    sourceContentOpened, oneDriveHydrationRequested, videosDecoded,
    videoJobsEnqueued, derivativesPublished
$report.availability | Format-Table status, count -AutoSize

if ($report.candidateCount -ne 12) {
    Write-Warning "Candidate count differs from the earlier 12-video snapshot; review current revision/exclusions before further work."
}
if (-not $report.readOnly -or
    $report.sourceContentOpened -or $report.oneDriveHydrationRequested -or
    $report.videosDecoded -or $report.videoJobsEnqueued -or
    $report.derivativesPublished) {
    throw "Audit violated its declared safety envelope."
}
if ((($report.availability | Measure-Object -Property count -Sum).Sum) -ne
    $report.candidateCount) {
    throw "Availability aggregation does not reconcile."
}
~~~

**Expected:** `readOnly = True`, `sourceContentOpened = False`,
`oneDriveHydrationRequested = False`, `videosDecoded = False`,
`videoJobsEnqueued = False`, `derivativesPublished = False`.
If the catalogue is unchanged, expect `candidateCount = 12`.
**Maintainer acceptance, 2026-11-03:** after PR #726 merged, the
read-only production audit returned `candidateCount=12` and exactly
`local-metadata-match=12`; `readOnly=true`,
`sourceContentOpened=false`, `oneDriveHydrationRequested=false`.
All 12 currently appear local with matching sizes, but SHA, full-range
signalling and visual colour remain unverified.

A fresh database inventory is not needed solely to rerun the
previous 58-video census. Share the aggregate `candidateCount`
and `availability` counts, not source filenames or DB credentials.
The local state should be unchanged after the command.

## Checkpoint 6 — explicit private UUID shortlist, AFTER PR merge

The normal `video compatibility local-check` command remains
**aggregate-only**. This separate `local-candidates` command needs
a deliberate, once-only `--show-revision-ids` flag. Only
current, nonexcluded Rec.709 `yuvj420p` pixel-only revisions with
**matching local file attributes and size** appear in the
private local results; no filename, path, SHA or user media is exposed.
Neither this command nor displaying a UUID authorizes an original
read, decode, transcode, hydration or processing job.

Start **only after the PR is merged**, on local `main`.
CI owns documentation validation; do not run docs checks locally.

```powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main

$db = "photoidentity_main"
$envName = "PHOTOIDENTITY_POSTGRES_CONNECTION_STRING"

# Explicitly permit UUID display ONLY in this private terminal.
$lines = & dotnet run --project src/PhotoIdentity.Cli --configuration Release -- video compatibility local-candidates --postgres-connection-env $envName --database $db --show-revision-ids
if ($LASTEXITCODE -ne 0) {
    throw "WI-0210 local revision selection failed."
}
$selection = ($lines -join "`n") | ConvertFrom-Json -ErrorAction Stop

if (-not $selection.readOnly -or
    -not $selection.revisionIdsShown -or
    -not $selection.operatorSelectionRequired -or
    -not $selection.localIsNotIntegrityProven -or
    $selection.sourceContentOpened -or
    $selection.oneDriveHydrationRequested -or
    $selection.videosDecoded -or
    $selection.jobsEnqueued -or
    $selection.derivativesPublished -or
    $selection.locallyMatchedCount -ne @($selection.candidates).Count) {
    throw "WI-0210 local revision selection safety check failed."
}

$selection | Select-Object candidateCount, locallyMatchedCount, readOnly,
    sourceContentOpened, oneDriveHydrationRequested

# PRIVATE TERMINAL ONLY: do not paste revision UUIDs or paths into
# shared chat, PRs, tickets, logs or documentation.
$selection.candidates | Format-Table revisionId, sourceBytes,
    durationSeconds, codedOrientation, hasAudio -AutoSize
```

Expected if unchanged: `candidateCount=12`,
`locallyMatchedCount=12`, 12 private UUIDs, all safety flags
`False`. The shortlist is ordered by shorter duration, then
source bytes, then UUID, but **does not automatically select**
a file. Its orientation is **coded**, not the
rotation-adjusted display orientation. The local classification
may change between calls.

**Stop after viewing the shortlist.** Do not run
`--read-local-bytes` or FFprobe automatically. A separate,
deliberately authorized exact-revision integrity stage must
recheck exclusions, revision currentness, availability,
no-recall source SHA and process controls before any
original-media use. The already implemented
`video one-shot source-proof` uses explicit
`--read-local-bytes`; it is **not invoked here**.

Share only `candidateCount`, `locallyMatchedCount`,
and whether the safety checks passed, not the actual UUIDs.

## What follows

If at least one candidate is `local-metadata-match`, prepare a
separately reviewed, private exact-revision source proof using
the existing no-recall reader, exclusion and source-revision fencing,
approved pinned FFprobe/FFmpeg and owned scratch; that **explicit**
operator step opens local original bytes and must not be silently
performed by this command. If no candidate is confirmed local, stop:
do not request OneDrive hydration as part of WI-0210's audit.
WI-0207 separately owns managed video hydration.

The selected WI-0210 production target remains **Rec.709 explicit
full-range `yuvj420p` to playable 720p H.264/AAC proxy**, not poster,
frames, HDR or missing-rotation admission. Real visual colour/contrast,
rotation, local-source safety, unchanged SHA-256 and the normal
published playback path remain pending.
