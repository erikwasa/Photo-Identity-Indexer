# WI-0190 checkpoint 5 — FrameEvidence worker lease and publication boundary

**Development-only, unregistered.** The maintainer reported that the
authoritative local v37→v38 migration verification was successful on
2026-10-31. No exact restored table counts were shared with this report.
The v38 schema, private caption/vector tables and source exclusion purge
are now available on the maintained host. **There is still no automatic or
manually activated real-video FrameEvidence worker** and no new API.

## New execution contract

1. An **existing approved video control** (off/paused by default) and
   existing idempotent `FrameEvidence` queue job are required; the worker
   does not enqueue, enable or change video control.
2. `VideoWorkerAttemptCoordinator` asks the PostgreSQL queue to claim only
   `FrameEvidence`, not any older playable-proxy or face-evidence job.
   Existing playable-proxy adapters likewise select only PlayableProxy.
3. The **unregistered** `VideoFrameEvidenceWorkerAdapter` rechecks the
   current lease, operator generation, cancellation and photo priority
   before and after the producer's work.
4. A ten-second timer renews the 60-second video job lease and rechecks
   the authoritative live video safety probe; if an operator pauses,
   cancels, loses photo priority or excludes the revision, the token
   is cancelled. A noncooperative producer still cannot commit because
   v38 revalidates within the final transaction.
5. An injected future producer returns a complete
   `VideoFrameEvidenceProduction` sampling plan and in-memory captions/
   vectors. The adapter checks exact revision/sampling/frame count,
   then atomically publishes to private PostgreSQL via the v38 repository.
   The producer is **not yet implemented** or registered for real media.
6. The coordinator reads back the durable current batch, checks the
   revision, frame count and succeeded job state; an in-memory report
   claiming success is insufficient. No private captions or vectors are
   included in the worker's public/sanitized status.

## CI-only verification

The new integration test requires a disposable PostgreSQL administrator
connection (the maintained database must NEVER be provided to the
`PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING` test variable).

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~VideoFrameEvidenceWorkerAdapterTests"
dotnet test tests/PhotoIdentity.Persistence.Tests/PhotoIdentity.Persistence.Tests.csproj -c Release --filter "FullyQualifiedName~VideoFrameEvidencePublicationTests"
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

**Test skip warning:** The PostgreSQL test method returns without running
database assertions when the disposable admin environment variable is
missing. CI supplies the isolated PostgreSQL server. Do not interpret a
locally green test with no isolated DB as a successful persistence proof.

## Local actions

**No additional local verification is required for checkpoint 5.** This
checkpoint does not introduce a migration after v38, new runtime endpoint,
Azure traffic or real-video processing. Continue to use the maintained
v38 catalogue only after the earlier successful isolated migration gate.
Normal photo and video proxy functionality must remain unaffected.

When a future reviewed, opt-in **real-media producer** is implemented,
maintainer verification will be required for FFmpeg Job Object containment,
frame staging and scratch cleanup, Azure bridge routing and disclosure,
heartbeat/revocation races, unsupported/oversized clips, and representative
quality/storage/cost under mixed photo load.

The prior registered proxy pilot already established transient real video
frame decoding and Azure CLIP/Ollama inference. It did **not** persist
frame evidence or authorize unattended processing. These are distinct
acceptance gates.
