# WI-0188 integrated publication checkpoint — schema v36

**Status: development integration, NOT permission to process real videos.**
This batch deliberately brings the existing isolated lane closer to a
production worker without installing any worker, an FFmpeg encoding profile,
a queue selector or an operator enable switch.

## Integrated code path

1. Existing v35 queue admission selects at most one eligible video when the
   caller has explicitly opted in, PostgreSQL video processing has been
   enabled in an **isolated fixture only**, and current photo work is idle.
2. `VideoUnpublishedToolAttempt` checks a live lease; prepares a hash-verified,
   already-local staged MOV/MP4; supervises an approved binary inside Windows
   suspended-launch Job Object limits; continues lease heartbeats and
   photo-priority checks.
3. An optional guarded callback fires only after successful child exit and a
   fresh safety observation, **before the owned tool workspace is deleted**.
   **Publishing requires native suspended Windows Job Object execution with
   a one-process cap** so no surviving descendant can write during commit;
   uncapped/multi-process execution cannot invoke a publication callback.
   The unregistered `VideoPublishedToolAdapter` wires this callback to
   `VideoDerivativeFilePublisher`, which requires the exact `output.mp4`
   in a marked direct-child `video-tool-<guid>` scratch workspace.
4. The publisher verifies private cache-root ownership, volume capacity,
   source-root separation, absence of reparse points, bounded size and SHA-256.
   It moves the output on the same volume to an opaque private
   `video-derivatives/<job>/<lease>.mp4` name, with **no overwrite**.
5. `PostgresVideoPublishedDerivativeRepository` locks the source row and
   current video control generation, then atomically validates lease,
   cancellation, revision, supported evidence and exclusion, and writes the
   v36 derivative receipt **together with** the job's `succeeded` state in
   one PostgreSQL transaction. The generic job-success method still refuses
   unfenced success. Coordinator success requires both state and a visible
   durable v36 receipt.

No public API serves the derivative bytes. Source originals are never
passed to the subprocess. There is no automatic video activation.

## What "atomic" means, and what it does not

The `video_published_derivatives` row and `succeeded` job status commit
atomically **within PostgreSQL**. The prior same-volume rename is a
filesystem operation, not part of the database transaction. A power loss
or ambiguous database commit after rename can leave an **orphaned but
unreferenced file**. Such an orphan MUST NEVER be served; eventual deletion
requires a future database-aware janitor, not a broad recursive delete.
A known rejected lease removes its newly named output.

A committed record also does not prove browser playback. The current
synthetic tests intentionally use fake output bytes. Before activation,
WI-0189 must add fixed, audited FFmpeg arguments and independent MP4
codec/track/seek/rotation/audio validation, plus per-file and aggregate
retention/eviction. WI-0188 must still prove hard filesystem/IO quotas,
network isolation or an explicitly approved substitute, GPU-disabled CPU
profile, resilient restart and performance isolation under real loads.
The publisher accepts *only* PlayableProxy jobs; no caption/frame/face
publication is authorized by it. Do not register its adapter yet.

## Required single maintained-host migration gate: v34 -> v36

The maintainer's last accepted maintained catalogue is **v34**; v35 has not
yet been rehearsed. Do **not** run a v36-built application, CLI operator
command, or deployed package against the maintained v34 database until:

1. Stop Photo Identity fully, keeping PostgreSQL available. Determine the
   authoritative launcher-connected database; do not assume an older
   `photoidentity_rehearsal_*` database is production.
2. Take a **new authoritative backup** of that confirmed database and verify
   a **new isolated restore** using the existing root scripts. Details of
   safely deriving the credentials from the launcher remain in
   [the previous control runbook](video-processing-control-v35.md).
3. Require the JSON restore report to say `passed=true`, restored source
   schema `34`, a different source and verification database, and a safe
   `photoidentity_restore_*` verification target.
4. Run **one** schema rehearsal against the verified restored database, never
   the maintained one:

~~~powershell
$ErrorActionPreference = 'Stop'
$databaseName = 'REPLACE_WITH_CONFIRMED_APP_CONNECTED_DB'
$backupPath = Join-Path $env:LOCALAPPDATA (
  'PhotoIdentity\backups\postgresql\pre-wi0188-v36-' +
  (Get-Date -Format 'yyyyMMdd-HHmmss') + '.dump'
)
.\backup-postgres-catalogue.ps1 -DatabaseName $databaseName -OutputPath $backupPath
.\verify-postgres-backup-restore.ps1 -BackupPath $backupPath -ApplicationStopped -RetainRestoredDatabase
$restoreReportPath = "$backupPath.restore-verification.json"
$report = Get-Content -LiteralPath $restoreReportPath -Raw | ConvertFrom-Json
if (-not $report.passed -or [int]$report.restoredEvidence.schemaVersion -ne 34) {
    throw 'A verified isolated schema-v34 restore is mandatory.'
}
# Set only a process-scoped environment variable containing the existing
# launcher PostgreSQL connection string; never paste or print credentials.
# Example name used below: PHOTOIDENTITY_WI0188_V36_REHEARSAL
dotnet run --project src/PhotoIdentity.Cli -c Release -- catalogue rehearse-v36 `
  --postgres-connection-env PHOTOIDENTITY_WI0188_V36_REHEARSAL `
  --restore-report $restoreReportPath
if ($LASTEXITCODE -ne 0) { throw 'Isolated v34->v36 rehearsal failed.' }
Remove-Item Env:\PHOTOIDENTITY_WI0188_V36_REHEARSAL -ErrorAction SilentlyContinue
~~~

The existing script automatically runs *both* migrations v35 and v36 in
the one isolated restore. Expected output: `Schema v34 -> v36 rehearsal
passed`, unchanged original catalogue/photo/video/queue table counts,
exactly **two** new migration rows, v35 control **disabled/paused**,
an **empty** v36 derivative receipt table, and repeated initialization PASS.
This is one consolidated local gate; no separate maintained v35 startup is
required.

Only after the rehearsal passes, run/deploy the v36 package normally and
require health `schemaVersion: 36`, unchanged photo features and no active
video worker. Do not use an operator resume action as an enable switch;
the lane still has **no** production enable command.

## Conservative v36 output recovery

Checkpoint 13 adds a **separate, dry-run-first** DB-aware orphan reconciler
that may remove only old **unregistered** files belonging to known terminal
video jobs, under a stopped video lane and exclusive control lock.
Registered but invalidated outputs remain protected and are reported
separately for WI-0189's reviewed privacy/retention lifecycle.
See [operator recovery](video-derivative-recovery-v36.md).
This introduces **no new migration** and does not replace the existing
v34→v36 isolated rehearsal safety gate.

## Experimental format-validation handoff

Checkpoint 14 adds an **unregistered** fixed SDR H.264/AAC 720p candidate
adapter and a separately constrained hash-pinned FFprobe verification step
before the existing v36 durable publisher. It is not a production activation,
and no executable is bundled. See
[the codec validation handoff](video-proxy-pilot-validation-v36.md).
The actual proxy profile, browser/seek/audio acceptance and quotas remain
WI-0189 responsibilities, while resource isolation and controlled activation
remain WI-0188 requirements.

## Registered privacy and retention lifecycle (checkpoint 16)

A separate bounded, dry-run-first
[registered derivative maintenance](video-registered-derivative-lifecycle-v36.md)
command now revokes invalidated, missing and optionally hash-corrupt
receipts before deleting the known owned private bytes; it can plan a
registered-byte budget and requires the video lane fully stopped.
A separately approved UUID-only requeue handles reversible exclusions
and deliberate regeneration. This does **not** change schema v36,
automatically enforce a hard disk quota, or activate a worker.
The old orphan cleanup still protects all registered receipts.

## Additive v37 exact-job operator scope

Checkpoint 17 advances the video-only schema to **v37** with a nullable
selected job ID and absolute expiration. It has **no new video data by
default and does not enable the lane on migration**. The
`catalogue rehearse-v37` command uses the already-isolated v34
restored-database rehearsal and applies v35→v37 in one sequence; older
`rehearse-v35` / `rehearse-v36` entry points are compatibility
aliases applying the current version. The existing maintained v34
backup and isolated rehearsal requirement must now be read as a
**fresh v34→v37 rehearsal**, not v34→v36.

[Full selected one-shot protocol and no-go boundaries](video-scoped-one-shot-v37.md).
This adds no unattended video worker or release approval.

## CI and remaining Windows proof

Tests cover canonical job/lease path names, unowned-output rejection,
bounded synthetic output, state/receipt transaction, revoked generation,
exclusion invisibility, callback ordering and rejection cleanup, as well
as pre-existing photo-priority, staging and Windows Job Object tests.
Disposable database integration tests use the opt-in
`PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING` fixture.

Maintained-host acceptance is **not requested yet** for this unregistered
checkpoint. Future consolidated Windows acceptance must measure OneDrive
no-recall behavior; real media quality, audio/orientation and seeking;
source exclusion and revocation; photo throughput plus search/slideshow
latency against baseline; and private orphan recovery/eviction. Never paste
media paths, connection strings, hashes or source IDs into public issues.


> WI-0208: this rehearsal deliberately uses `-RetainRestoredDatabase` because the next CLI step needs the existing restored database. Successful standalone verification normally deletes its owned restored copy. After the rehearsal, use the exact-name, ownership-validated `manage-postgres-restores.ps1` procedure in [PostgreSQL operations](postgresql-operations.md); do not delete production or backup dumps.
