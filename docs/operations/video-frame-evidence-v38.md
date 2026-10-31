# WI-0190 checkpoint 4 — private frame evidence, PostgreSQL v38

**Status:** development-only, no automatic frame worker, API or retrieval
index. Do **not** run the new v38 application against the maintained v37
catalogue before a fresh PostgreSQL backup and isolated upgrade rehearsal.

## What v38 adds

- `video_frame_evidence_batches` (one durable receipt per `FrameEvidence`
  job): revision, sampling and extraction versions, caption/CLIP hashes,
  prompt/preprocessing identity, frame count, vector dimensions, timestamp.
- `video_frame_evidence_frames` (indexed by job and ordinal): exact
  timestamp, optional scene index, bounded caption and deterministic
  little-endian float32 vector bytes. No image bytes or original paths.
- `PostgresVideoFrameEvidenceRepository.TryPublishAsync`: previously
  computed complete in-memory batch and a live `FrameEvidence` lease,
  then source/control/job fencing and a single transaction containing
  succeeded job state, batch and every frame.
- `GetCurrentAsync`: private current-only read; guards against incomplete
  rows, excluded/deleted/stale revisions and unsupported metadata.
- A v38 source-copy exclusion trigger physically deletes matching
  private evidence batches and their frame rows (FK cascade) within the
  exclusion transaction; source locks serialize publication with exclusion.
  Revision deletion also cascades. Worker-layer purge/retention auditing is
  not yet wired.

The migration creates private frame tables, ensures the exact existing
source-copy exclusion table contract is present (it was lazily initialized
in v37), and adds an exclusion-purge trigger. It does not backfill old
video jobs, enable video execution, upload frames or rewrite legacy data.
Readiness defaults unchanged. PostgreSQL remains the only writable store.

## CI verification without private media

```powershell
dotnet test tests/PhotoIdentity.Core.Tests/PhotoIdentity.Core.Tests.csproj -c Release --filter "FullyQualifiedName~VideoFrame"
dotnet test tests/PhotoIdentity.Persistence.Tests/PhotoIdentity.Persistence.Tests.csproj -c Release --filter "FullyQualifiedName~VideoFrameEvidencePublicationTests"
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

The persistence test needs
`PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING` pointing at a
**disposable isolated PostgreSQL server**. When missing, the database
test method returns without testing PostgreSQL; a green test command
alone therefore does not prove the DB behavior. Windows CI runs its own
isolated PostgreSQL instance.

## Mandatory maintained-catalogue v37→v38 gate

1. Keep the current v37 Photo Identity application stopped and identify
   its authoritative PostgreSQL database (for this maintainer,
   `photoidentity_main`; do not use a rehearsal database as production).
2. Take a **new, verified authoritative backup** with
   `backup-postgres-catalogue.ps1`.
3. Run `verify-postgres-backup-restore.ps1` with
   `-ApplicationStopped`. Require `passed=true`, source schema 37,
   a different isolated restore database, and original row counts intact.
4. Rehearse schema v37→v38 **only on that isolated restore**. The
   `catalogue rehearse-v38` command accepts only a verified successful
   `photoidentity_restore_*` target whose source version is exactly 37.
   It checks all pre-upgrade table counts against the restore report,
   applies the latest schema twice for idempotency, preserves the existing
   table counts except the migration-history increment, verifies the
   v38 migration record, both empty frame-evidence tables and singleton
   video control retention.
5. Only after the rehearsal passes, deploy a v38-built app to the
   maintained catalogue; recheck health schema 38, existing photo
   features, video job/derivative counts, operator control settings, and
   no new OneDrive hydration or media writes.

PowerShell commands from the repository root, with the application stopped:

```powershell
$databaseName = "photoidentity_main"
$backupPath = Join-Path $env:LOCALAPPDATA (
    "PhotoIdentity\\backups\\postgresql\\pre-wi0190-v38-" +
    (Get-Date -Format "yyyyMMdd-HHmmss") + ".dump"
)
.\\backup-postgres-catalogue.ps1 -DatabaseName $databaseName -OutputPath $backupPath
.\\verify-postgres-backup-restore.ps1 -BackupPath $backupPath -ApplicationStopped

$reportPath = "$backupPath.restore-verification.json"
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if (-not $report.passed -or [int]$report.restoredEvidence.schemaVersion -ne 37 -or
    $report.sourceDatabase -ne $databaseName -or
    $report.verificationDatabase -eq $databaseName) {
    throw "Isolated v37 restore verification did not pass."
}

# Supply an existing LOCAL administrator/maintainer connection string
# through an environment variable; never echo it or paste credentials.
# The command ignores the database in the connection string and uses
# ONLY the verified isolated restore target from $report.
$env:PHOTOIDENTITY_WI0190_REHEARSAL = $env:PHOTOIDENTITY_WI0190_CONN
if (-not $env:PHOTOIDENTITY_WI0190_REHEARSAL) {
    throw "Set the existing local PostgreSQL connection variable first."
}
dotnet run --project src/PhotoIdentity.Cli -c Release -- catalogue rehearse-v38 `
    --postgres-connection-env PHOTOIDENTITY_WI0190_REHEARSAL `
    --restore-report $reportPath
if ($LASTEXITCODE -ne 0) { throw "Isolated v37-to-v38 rehearsal failed." }
Remove-Item Env:PHOTOIDENTITY_WI0190_REHEARSAL -ErrorAction SilentlyContinue
```

**Important:** Running `dotnet run` against the maintained catalogue with
the new build before this isolated verification can trigger automatic
schema initialization. Do not substitute an application startup for this
rehearsal. Confirm the backup and restore scripts' available parameters
with `Get-Help` if your installation differs.

## Remaining acceptance work

Implement a deliberate `FrameEvidence` worker and fully verified proxy
staging, lease heartbeat and bounded caption/extraction execution; add
currentness/revocation/physical exclusion-purge checks in the worker;
wire opt-in publication; evaluate representative private videos and
retrieval quality; then provide locally verifiable v38 deployment steps.
A sampled caption cannot establish motion, audio, speech or unsupported
events between captured frames. Keep originals and personal evidence
outside the repository.
