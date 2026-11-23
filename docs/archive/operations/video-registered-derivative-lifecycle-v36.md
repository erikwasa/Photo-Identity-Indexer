# WI-0188 — registered video derivative revocation, quota and explicit requeue (v36)

**Development-only. No worker activation, media API or automatic deletion.**
The existing v36 publication registry makes it possible for a generated video
file to be invisible on exclusion or revision change yet still occupy local
disk. The earlier orphan cleanup deliberately protects **all** registered
files, including invalidated receipts. This follow-on introduces a separate
**operator-only** maintenance lifecycle for those registered outputs.

## Contract

- The cache root is explicitly provided and must have the existing precise
  application-owned marker, absolute local unlinked ancestors, and no overlap
  with any registered local source root. These rules are shared with the
  v36 publisher. No original video is opened, modified or hydrated.
- The command accepts a **current v36** PostgreSQL database on localhost.
  It does **not** migrate schema or infer the application's database name.
  The whole database phase locks the video operator control row and requires
  `enabled=false`, `paused=true`, and **zero running video leases**.
- The database locates only its registered `video_published_derivatives`
  receipts and checks the exact source/revision/exclusion/inspection fences
  used for publication. Invalidated source copies, superseded revisions,
  deleted videos, unsupported evidence, a missing private file, or a byte
  length/hash mismatch (when requested) can be revoked.
- Optional **registered-byte budget** (`--quota-mib`, 64–16384 MiB):
  oldest eligible current derivatives are selected after invalidated ones
  when the registered sum exceeds the budget. Healthy quota evictions
  require a creation age of at least 24 hours; invalidated/missing/corrupt
  evidence does not wait for an age threshold.
- Up to **32** receipts per run. One additional selected row lets the report
  signal `moreCandidates=true`; repeated bounded dry runs/maintenance
  can make further progress. This is a logical registered-byte budget, **not**
  an OS filesystem quota or instant byte reservation. Unknown/unregistered
  files must be handled by the separate orphan janitor.
- Dry run (default) reports aggregate counts and projected registered bytes
  without exposing any source path, content hash, file name, or source/job ID.
  `--verify-hashes` additionally streams SHA-256 of selected registered
  cached files; otherwise a wrong-size file is detected, but same-size
  corruption remains undetected.
- **On `--apply`, receipt revocation and succeeded→failed
  (`failure_code='revision-invalid'`) are one PostgreSQL transaction**.
  It must commit before any private unlink. The original durable identity
  and profile fingerprint stay intact; no queued work is created by purge.
  Only after successful DB commit does the process unlink the exact
  canonical generated MP4. Failure to unlink is reported with a nonzero
  CLI exit status, leaving an **unreferenced and invisible orphan** for
  the prior stopped-lane `video derivatives recover` command. Disk and
  database are NOT one atomic resource, so no guarantee of immediate
  physical privacy purge is implied by DB revocation alone.

This operation may make an eligible derivative temporarily unavailable.
It never claims that deleted bytes are still playable. Existing photo
catalogue, searches, slideshow and original video archive remain untouched.

## Operator workflow — to use only after the consolidated v36 local gate

The maintained database was last accepted at **v34**. **Do not** run the v36
application, CLI, or any operator database command against the maintained
v34 catalogue before a **new authoritative backup**, a **verified isolated
v34→v36 migration rehearsal**, and the approved maintained upgrade from
[the consolidated v36 runbook](video-publication-and-v36-rehearsal.md).

The examples below are for a later already-v36 Windows maintenance session;
they are **not a request to run these commands now**.

~~~powershell
$ErrorActionPreference = 'Stop'
$db = 'REPLACE_WITH_CONFIRMED_APPLICATION_DATABASE'
$envVar = 'REPLACE_WITH_EXISTING_LAUNCHER_POSTGRES_CONNECTION_ENV_NAME'
$privateCache = 'C:\REPLACE_WITH_MARKED_PRIVATE_VIDEO_CACHE'

# 1. Confirm no enabled/active video work.
dotnet run --project src/PhotoIdentity.Cli -c Release -- video jobs status `
  --postgres-connection-env $envVar --database $db

# 2. Read-only preview. Optionally verify cached bytes more thoroughly:
dotnet run --project src/PhotoIdentity.Cli -c Release -- video derivatives maintain `
  --postgres-connection-env $envVar --database $db `
  --cache-root $privateCache --max-records 16 --verify-hashes

# 3. Optional byte-budget preview, without a delete:
dotnet run --project src/PhotoIdentity.Cli -c Release -- video derivatives maintain `
  --postgres-connection-env $envVar --database $db `
  --cache-root $privateCache --quota-mib 1024 --min-age-hours 48

# 4. ONLY after operator review of eligible counts and successful checks:
# Repeat the identical preview command with --apply appended.
~~~

For preview, inspect `invalid`, `missing`, `corrupt`,
`quotaSelected`, `planned`, `registeredBytesBefore`,
`projectedBytesAfter`, and `moreCandidates`. `dryRun=true`,
`revoked=0`, and `filesDeleted=0` are mandatory. If
`deleteFailures>0` on apply, investigate local handles/permissions,
keep the video lane stopped, and use the older *separate* orphan
recovery dry run. Never manually recursively delete the private root.

## Regeneration after temporary exclusion or quota eviction

A revoked job is **terminal failed**, never silently reactivated. A
separate operator command allows exactly one selected immutable job ID
to be requeued, but only if source/revision inspection is once again
**current, supported, unexcluded and not deleted**; no registered receipt
may still exist. It requires the same stopped lane and zero running jobs,
resets failed attempts, and does **not** enable video workers, open media,
or publish bytes. Its default is dry run.

~~~powershell
$jobId = 'REPLACE_WITH_APPROVED_REVOKED_JOB_UUID'
dotnet run --project src/PhotoIdentity.Cli -c Release -- video derivatives requeue `
  --postgres-connection-env $envVar --database $db --job-id $jobId
# Only after reviewing the exact identity and restoring its valid source:
# Repeat with --apply
~~~

Requeue does not guarantee the source is locally hydrated or that a
matching encoder is installed; those checks remain the future worker's
lease-fenced responsibilities. A superseded revision may never requeue
under the old identity. New content/preset requires a new deliberate job.

## Deliberate remaining blockers

No automatically scheduled cleanup, hard disk/IO quota, network sandbox,
user-facing retention selector, real-video application worker or
media-serving endpoint exists. Browser seeking/quality, actual camera
codec compatibility, HDR and color handling still require WI-0189.
The separate source exclusion service's full privacy purge integration
and photo-only vs mixed-workload performance acceptance are still pending.
Full repair/regeneration tests must use representative **already-local**
MOV/MP4 in the consolidated maintained Windows verification session.
The private registry and generic photo features are not exposed by this
operator-only command.
