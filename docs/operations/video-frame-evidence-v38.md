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

The migration only creates tables and indexes; it does not backfill old
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
4. Rehearse schema v37→v38 **only on that isolated restore**. Use the
   repository's supported schema rehearsal command and ensure that it
   validates all v38 tables, empty frame evidence rows and unchanged old
   table counts before allowing the maintained instance to migrate.
5. Only then deploy a v38-built app to the maintained catalogue; recheck
   application health schema 38 and that video controls remain disabled/
   paused when expected, with ordinary photo workflows unchanged.

This checkpoint does not yet add a dedicated automated v38 restore
rehearsal command. Until one is added and passes the above gates,
**do not migrate the maintained catalogue solely to test this library**.
Do not execute `dotnet run` against the maintained database using
a v38 build as a substitute for the rehearsal.

## Remaining acceptance work

Implement a deliberate `FrameEvidence` worker and fully verified proxy
staging, lease heartbeat and bounded caption/extraction execution; add
currentness/revocation/physical exclusion-purge checks in the worker;
wire opt-in publication; evaluate representative private videos and
retrieval quality; then provide locally verifiable v38 deployment steps.
A sampled caption cannot establish motion, audio, speech or unsupported
events between captured frames. Keep originals and personal evidence
outside the repository.
