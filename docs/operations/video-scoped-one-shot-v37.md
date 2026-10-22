# WI-0188 — explicitly selected one-shot video trial, schema v37

**Experimental, no unattended worker.** This is an opt-in single-attempt
development path for already-local, previously ingested and technically
inspected Rec.709 MOV/MP4 revisions. There is no automatic video scanner,
hydration, hosted video service, web video endpoint, photo job change,
arbitrary archive path argument or general-purpose FFmpeg command runner.

## Current executable safety gate (2026-10-22)

**Real-media mutations remain disabled at the CLI boundary.** Following the
synthetic AppContainer ACL feasibility work in merged PR #650, the source,
tool-loader, network and storage boundaries are still not verified for a
private-video run. Until separately reviewed acceptance evidence exists,
`video one-shot enqueue --apply` and
`video one-shot run --apply --acknowledge-isolated-v37-rehearsal`
return a nonzero exit and the fixed sanitized reason
`media-isolation-not-approved`. They do so **before** opening PostgreSQL,
reading source availability, touching cache roots, staging bytes, arming a
lease, spawning FFmpeg/FFprobe, or publishing any derivative. The response
uses `dryRun=true` to reflect that nothing was executed or enqueued.

Earlier instructions later in this file describe the **intended future**
one-shot workflow, **not a currently usable authorization procedure**.
No environment variable, acknowledgement flag or operator-provided tool hash
may override the current hard stop. Ordinary read-only candidate, enqueue
preview, environment and source-proof commands remain separate and unchanged.
Removing this stop requires another reviewed code change after the independent
network, real-path ACL, hard storage and mixed-photo-load gates are satisfied.
Passing a synthetic lab does not automatically lift it.

## Why v37 is necessary

The v34 queue normally takes the oldest eligible video job when a
persisted lane is enabled. An opt-in trial must never accidentally process
*another* queued job. The additive v37 migration introduces a nullable
`selected_job_id` and `authorization_expires_at_utc` on the **video-only**
operator control singleton, leaving both NULL and video enabled=false /
paused=true on upgrade. A scoped arm sets both under a control lock and
increments the generation. Queue selection checks the exact job ID,
authorization deadline and ordinary source/revision/exclusion/photo priority
gates. Heartbeats, staged-source locators, live photo-first safety probes,
retry acknowledgement and final receipt publication all recheck the
selected ID, absolute PostgreSQL deadline and generation.

The fixed authorization window is **13 minutes**. The selected tool's
wall time is capped at 9 minutes and the coordinator attempt at 11 minutes,
leaving time for bounded input staging and independent FFprobe verification.
A stopped or crashed CLI cannot keep the selected job authorized after
its deadline. If the process crashes before disarming, operators must use
the existing `video jobs stop --apply` after inspecting the queue; do not
manually bypass the scope. **No polling or automatic retries are started.**

## Maintained v37 preflight gate accepted — subsequent operator report (2026-10-21)

The original design paragraphs below describe the **requirements at the
time of implementation** (then an unverified v34→v37 upgrade). The
maintainer subsequently confirmed PASS for a fresh authoritative
PostgreSQL backup, isolated restore, v34→v37 rehearsal, preserved
catalogue table counts, repeated initialization, maintained PostgreSQL
health at v37, and disabled/paused/unselected video control. Photo
regressions, read-only Videos library/posters and no unintended
OneDrive hydration/original changes also passed. The database-free
approved FFmpeg/FFprobe synthetic pilot passed for 3-second landscape
with AAC and portrait without audio.

**These prerequisites no longer need rerunning solely for the next
read-only selection step.** They do not independently authorize
executing the real-video `--apply` path or claim real-media IO quotas,
mixed-load isolation, exclusion/restart/cancellation acceptance or
browser playback.

## Read-only shortlist before the two deliberate actions

The optional command below identifies *potential* Rec.709 SDR input
revision UUIDs from current v37 catalogue **metadata only**. It never
opens media, performs OneDrive availability/hydration calls, runs tools,
creates jobs or enables the video lane:

~~~powershell
# The same already-verified, process-scoped PostgreSQL connection variable
# and authoritative maintained v37 database used for successful read-only
# video jobs status; never paste or print the connection string.
$connectionVariable = 'PHOTOIDENTITY_M30_VERIFY_PG'
# $databaseName must already name the confirmed launcher-connected database.

dotnet run --project src/PhotoIdentity.Cli -c Release -- video one-shot candidates `
  --postgres-connection-env $connectionVariable `
  --database $databaseName `
  --limit 12
if ($LASTEXITCODE -ne 0) { throw 'Video candidate metadata preflight failed.' }
~~~

The output protocol is `wi0188-one-shot-candidates-v1`. It returns
up to 12 `candidates`, each containing only `revisionId`,
`sourceBytes`, `durationSeconds`, `orientation` (explicitly
**coded** orientation; 90-degree camera display rotation may differ)
and `hasAudio`, plus a `hasMore` flag. It exposes no file path,
source key, filename, GPS, face/person or capture-date information.

**Important:** `localAvailabilityChecked=false`. Being shortlisted
is NOT evidence that a video file is already local or unchanged.
The operator must deliberately pick **one** UUID and run the existing
`video one-shot enqueue` **without `--apply`** as described below.
That stronger dry run checks exact current revision, current exclusion,
already-local availability metadata, source size and codec inspection,
again without opening the media. Do not enqueue or run real-video
processing until the distinct containment/mixed-load gates have been
reviewed. No automatic execution or new source path flag is present.

## Read-only selected-video and environment proof (2026-10-21)

After merging the metadata shortlist checkpoint, the maintainer ran
`video one-shot candidates` successfully. It returned **one** potential
coded-landscape Rec.709 video (12.3 s, 21,687,565 bytes, with audio).
The separate exact-revision `video one-shot enqueue` command, invoked
**without `--apply`**, then returned:

~~~json
{"protocol":"wi0188-selected-enqueue-v1","dryRun":true,
 "eligible":true,"jobId":null,"mediaOpened":false,"videoExecuted":false}
~~~

This **accepts the specific media-availability/inspection preflight
only**, not any FFmpeg execution, private publishing or photo-mixed-load
performance requirement. Nothing was queued. Do not commit archive paths
or the selected revision UUID in public tracking.

## New no-job Windows environment preflight — before any trial authorization

The experimental `video one-shot environment` command checks whether a
**future** one-shot might be safe to configure, without creating any
queued job. It independently checks current-schema local v37 PostgreSQL,
stopped/paused/unselected video control, no running video lease, no
authoritative photo work, ownership and catalogue-wide source separation
of an existing marked private cache, at least **512 MiB** estimated
scratch capacity **plus 512 MiB free headroom** on a fixed local
volume, and both physical nonlinked FFmpeg/FFprobe executables against
the previously approved exact SHA-256 hashes. It never opens original
media or runs either executable. It cannot accept `--apply`,
`--job-id`, `--revision-id` or `--source-path`.

Use an operator-owned private root *outside all archive and OneDrive-sync
folders*, on a local fixed disk, never reusing a real folder with
unknown contents. From PowerShell in the repository root:

~~~powershell
$ErrorActionPreference = 'Stop'
$cacheRoot = Join-Path $env:LOCALAPPDATA (
  'PhotoIdentity\\video-one-shot-readiness-' + [guid]::NewGuid().ToString('N')
)
if (Test-Path -LiteralPath $cacheRoot) {
  throw 'Private cache root already exists; stop and review.'
}
New-Item -ItemType Directory -Path $cacheRoot | Out-Null
[IO.File]::WriteAllText(
  (Join-Path $cacheRoot '.photoidentity-video-derivative-root'),
  'wi0188-video-derivative-root-v1'
)

# Use ONLY the previously approved physical, unlinked binary paths and
# matching SHA-256 variables from the successful synthetic pilot.
# Confirm the live maintained DB name and process-scoped PG variable.
dotnet run --project src/PhotoIdentity.Cli -c Release -- video one-shot environment `
  --postgres-connection-env PHOTOIDENTITY_M30_VERIFY_PG `
  --database $databaseName `
  --cache-root $cacheRoot `
  --ffmpeg $ffmpeg --ffmpeg-sha256 $ffmpegHash `
  --ffprobe $ffprobe --ffprobe-sha256 $ffprobeHash
if ($LASTEXITCODE -ne 0) { throw 'Video trial environment preflight failed.' }
~~~

Successful output includes `passed=true`,
`reasonCode=preflight-only`, and explicit `false` values for
`originalsOpened`, `oneDriveHydrationRequested`,
`jobsEnqueued`, `derivativesPublished`, and
`realVideoExecutionAuthorized`.

The check also **always** reports
`hardDiskIoQuotaVerified=false` and
`networkSandboxVerified=false`: available storage, Job Object RAM/CPU
caps and checked tool hashes are not OS-enforced disk-I/O or network
confinement. **A PASS is not permission to enqueue or encode the real
video.** The remaining containment and planned independent
original-integrity/mixed-load measurements must be reviewed before
any real-video `--apply` attempt. Keep the marked root available
for subsequent reviewed steps; never recursively clean another directory.

## No-recall original-integrity proof (optional, explicit media read)

**Maintainer observed:** after merging PR #641, `video one-shot
environment` passed with schema v37, `dryRun=true`,
`passed=true`, `reasonCode=preflight-only`, no original reads,
no hydration request, no jobs, and no published derivatives. Its
hard disk/IO quota, network sandbox and real-video authorization
flags remain **false**, as intended.

The follow-up `video one-shot source-proof` command offers a separate
source-integrity check for **the same exact revision UUID already
eligible in the earlier enqueue preview**. Because verifying the
catalogued SHA-256 means reading actual private video bytes, the
default command does NOT open any files or even connect to PostgreSQL:

~~~powershell
# Existing session values: $databaseName, $revisionId and
# process-only environment variable PHOTOIDENTITY_M30_VERIFY_PG.
# Do not paste original paths, source file hashes or credentials.
dotnet run --project src/PhotoIdentity.Cli -c Release -- video one-shot source-proof `
  --postgres-connection-env PHOTOIDENTITY_M30_VERIFY_PG `
  --database $databaseName --revision-id $revisionId
~~~

The result must say `status=not-run`,
`explicitLocalReadRequested=false` and
`originalBytesOpened=false`. Only if the maintainer explicitly
consents to read the **already-local** original bytes for checksum
verification, add the exact flag below:

~~~powershell
# OPTIONAL: explicitly reads this one current source's bytes,
# without opening for write or intentionally recalling cloud data.
dotnet run --project src/PhotoIdentity.Cli -c Release -- video one-shot source-proof `
  --postgres-connection-env PHOTOIDENTITY_M30_VERIFY_PG `
  --database $databaseName --revision-id $revisionId `
  --read-local-bytes
if ($LASTEXITCODE -ne 0) { throw 'One-shot source integrity check failed.' }
~~~

The tool rejects online-only, outdated, excluded, linked, unsupported
or oversized revisions. It rereads exact source identity/size/hash from
a read-only current-schema PostgreSQL transaction, refuses
photo-work activity, running video leases and enabled/unpaused/selected
video control, and uses the same
no-recall Windows handle as the bounded video stager, reads at most
256 MiB under a three-minute deadline, and checks SHA-256 against
the catalogued original digest plus file size, mtime and already-local
status after reading. Only fixed nonprivate reason codes are printed.
Unknown interrupted-read state is explicitly `null`.

Expected on success: `status=verified`,
`revisionDigestMatched=true`,
`originalBytesOpened=true`,
`sourceMetadataStable=true`,
`oneDriveHydrationRequested=false`,
`originalWriteAccessRequested=false` and
`realVideoExecutionAuthorized=false`.
This is **pre-trial** source integrity only, not a final assurance
that originals remain unchanged after a future video processing run.
No video processing or job creation is authorized by this command.

**A real video `one-shot enqueue --apply` or `one-shot run --apply`
is still OUT OF SCOPE** until hard disk/IO and network risks (or
explicitly approved bounded alternative) and mixed-photo load
monitoring have been reviewed and an individual trial separately
authorized. Do not repeat the successful environment/read-only
eligibility/synthetic/migration checks merely for this new checkpoint.

## Maintainer original-checksum PASS and future photo-load benchmark gate

The maintainer explicitly tested `video one-shot source-proof`:
the default command performed no source read, and the consented
`--read-local-bytes` invocation returned
`status=verified`, `revisionDigestMatched=true`,
`sourceMetadataStable=true`, with no write access, OneDrive
hydration request, video job or derivative. This accepts only
**pre-trial integrity** of the selected original, not its safety
after processing.

The next development slice provides the offline
`video one-shot assess-photo-load` evaluator for carefully collected
photo-only and observed video-competing samples. See the
[full operator evidence contract](video-photo-mixed-load-evidence.md)
for its bounded JSON format and commands. **Do not use it yet with
invented mixed data**: mixed samples require a separately authorized
single-video trial, actual independent process observation and
repeatable photo workload measurements. Numeric acceptance limits
must first be reviewed by the maintainer because WI-0150/ADR-0012
does not supply them.

Hard disk/I/O and network containment and real-media cancellation,
exclusion, restart and post-trial integrity are **not** discharged
by this offline comparison or by the earlier source proof.
No `one-shot enqueue --apply` or `one-shot run --apply` is authorized.

## Explicit one-shot job I/O accounting (new safety checkpoint)

The current single-video FFmpeg and independent FFprobe paths now
enforce sampled read/write **transfer counters** from their native
one-process Windows Job Objects. This applies only to the already
reviewed selected-one-shot path: FFmpeg is configured for
512 MiB observed read / 384 MiB observed write; FFprobe for
512 MiB observed read / 32 MiB observed write.

The supervisor checks current job-wide counters while the process
is running and again immediately after exit, **before publishing**
a validated derivative. It fails closed on either observed
over-budget count or unavailable accounting, preserving explicit
process kill-on-close and existing scratch cleanup. This requires
no operator change to the prior approved tool hashes or v37 schema.
No unattended background worker is enabled.

**Caveat:** this measure is **not** an OS disk-quota, instantaneous
I/O-rate throttle, low-privilege filesystem policy, network-denial
sandbox or guaranteed enforcement of peak/transient writes.
Parent process's staged source read and derivative publication
are also outside child job accounting. Source-level integrity,
exclusion, no recall, free-space headroom, Job Object CPU/memory
and absolute timeouts remain independent controls. The selected
one-shot must **not be activated** until the outstanding
filesystem/IO/network isolation review and controlled workload
approval are separately completed. See
[Windows tool resource limits](video-worker-windows-resource-envelope.md).

## Two deliberate operator actions

1. `video one-shot enqueue` accepts only a previously catalogued
   `--revision-id UUID`, explicit PostgreSQL environment variable,
   exact database, and approved FFmpeg SHA-256. The operation is **dry
   run by default**; it checks current/supported/SDR inspection, bounded
   source size, the OneDrive availability attribute and local file length.
   It does NOT open original video bytes or ask OneDrive to hydrate. With
   `--apply`, it atomically rechecks source/exclusion/currentness under
   the source lock and records an idempotent `PlayableProxy` queued
   job with exact tool/preset version. The returned UUID is the only
   accepted input to the later processing command. Creating a job does
   not grant any tool execution.
2. `video one-shot run` requires the exact previously queued UUID, an
   already-created **application-owned marked private cache** not
   overlapping sources, absolute local FFmpeg/FFprobe files and
   independently approved SHA-256 digests for both binaries. It rejects
   remote PostgreSQL, wrong schema, unknown flags and unapproved tool
   changes **before** permitting processing. Dry run does not arm,
   claim, open media, or execute FFmpeg. With both `--apply` and
   `--acknowledge-isolated-v37-rehearsal`, it locks the source and
   video control, refuses photo work / active video leases, verifies
   the queued job matches the exact codec preset and executable hash,
   and arms only that UUID for 13 minutes. The coordinator claims at most
   one job; the bounded Windows Job Object encoder stages only a verified
   no-recall local copy, independently validates H.264/Rec.709/MP4
   output via a second pinned guarded FFprobe, and commits the v36
   private receipt under the existing lease/source/control fences.
   **Finally it disarms** only the matching grant and leaves every
   unrelated queued job untouched.

The attempt uses only one video process at a time, a conservative
768 MiB Job Object RAM ceiling, 15% CPU hard cap, maximum input 256 MiB,
maximum registered reservation 512 MiB (source plus 256 MiB temporary
output), a fixed 9-minute encoder wall limit and one-process FFprobe
with its own 15-second budget. Live photo activity and operator pause,
stop, exclusion or lost lease preempt execution. These are **not**
comprehensive disk quota, I/O bandwidth, GPU or network sandboxes.
They do not imply the experiment is production-safe unattended.

## Maintained Windows safety gate — not requested by this PR

The maintained PostgreSQL database was last accepted at **v34**, while
repo code now targets **v37**. The user must not start ANY newer
database-connected application or CLI against the maintained database
until an authoritative **fresh PostgreSQL backup** and the documented
**isolated restored v34→v37 schema rehearsal** have succeeded. Use
`catalogue rehearse-v37` on the *isolated restored database only*;
legacy `rehearse-v35`/`rehearse-v36` aliases also apply through current
v37, but v37 is the descriptive name. Ensure the rehearsal verifies:

- unchanged restored photo/video catalogue table counts;
- existing v34 jobs preserved; newly added v35 control initialized
  disabled and paused;
- v36 private receipt registry initially empty;
- v37 migration applied exactly once with NULL selected/deadline fields,
  and repeated initialization idempotent;
- original authoritative catalogue **never opened** by rehearsal.

See the existing
[PostgreSQL backup/rehearsal runbook](video-publication-and-v36-rehearsal.md).
Do not infer a database name from a past rehearsal. Never print
PostgreSQL credentials.

Only **after** the independent migration and worker containment gates
are approved should a maintainer exercise the dry-run commands from
PowerShell, substituting explicitly confirmed values:

~~~powershell
$db = 'REPLACE_WITH_VERIFIED_V37_DATABASE'
$connectionVariable = 'REPLACE_WITH_EXISTING_POSTGRES_CONNECTION_ENV_NAME'
$revisionId = 'REPLACE_WITH_SELECTED_ALREADY_LOCAL_VIDEO_REVISION_UUID'
$privateRoot = 'C:\REPLACE_WITH_MARKED_PRIVATE_VIDEO_CACHE'

# Operator-approved, separately installed binaries (never download via CLI)
$ffmpeg = (Get-Command ffmpeg.exe -ErrorAction Stop).Source
$ffprobe = (Get-Command ffprobe.exe -ErrorAction Stop).Source
$ffmpegHash = (Get-FileHash -LiteralPath $ffmpeg -Algorithm SHA256).Hash.ToLowerInvariant()
$ffprobeHash = (Get-FileHash -LiteralPath $ffprobe -Algorithm SHA256).Hash.ToLowerInvariant()
# Independently compare both hashes to accepted tool approvals before use.

dotnet run --project src/PhotoIdentity.Cli -c Release -- video one-shot enqueue `
  --postgres-connection-env $connectionVariable --database $db `
  --revision-id $revisionId --ffmpeg-sha256 $ffmpegHash

# Only after this preview indicates eligible, add --apply to enqueue
# exactly the selected revision, and copy the returned opaque jobId.
$jobId = 'REPLACE_WITH_RETURNED_QUEUED_JOB_UUID'
dotnet run --project src/PhotoIdentity.Cli -c Release -- video one-shot run `
  --postgres-connection-env $connectionVariable --database $db `
  --job-id $jobId --cache-root $privateRoot `
  --ffmpeg $ffmpeg --ffmpeg-sha256 $ffmpegHash `
  --ffprobe $ffprobe --ffprobe-sha256 $ffprobeHash

# Explicit full trial requires BOTH flags appended after approved rehearsal:
# --apply --acknowledge-isolated-v37-rehearsal
~~~

The first successful one-shot requires representative **already-local**
SDR H.264/AAC, portrait/landscape and optional HEVC/PCM trials,
independent original SHA-256 pre/post, no-hydration evidence,
seeking/audio/quality checks and photo-only versus mixed throughput
and slideshow/search latency comparisons. **No local video processing
is requested now.**

## Failure and crash behavior

- No available local bytes, unexpected hash, active photo work, wrong
  preset, missing source revision, excluded video or unapproved tool:
  no selected-job arm.
- A claim/retry always passes the scoped selection, expiration and
  generation checks. Wrong, stale or unrelated jobs are never claimed
  while this grant is live.
- Tool failure can leave the selected job queued for a separately
  requested retry; no retry loop runs automatically.
- On cancellation, try/finally disarms using a bounded independent token.
  A lost database connection can prevent disarm; the PostgreSQL
  authorization deadline still prevents further source reading and
  publication. Recover/disarm the stale lane explicitly after inspection.
- A v36 receipt is not a public player URL and is invalidated by source
  exclusion/revision changes; privacy maintenance and quota/eviction
  remain separate operator commands.

This trial is not production activation. Hard filesystem and I/O quota,
network isolation, a reviewed real-video profile, source-copy privacy
purge integration, video playback APIs and 24/7 worker/scheduling are
still unfulfilled WI-0188/WI-0189/WI-0152 tasks.
