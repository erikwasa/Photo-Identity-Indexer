# WI-0188 — photo-first Settings-driven video processing

**Maintainer-revised acceptance (2026-10-22).** This replaces the old
concurrent photo/video benchmark, mandatory AppContainer and comprehensive
network isolation gates. The goal is to process videos after photos safely,
preserve existing photos/catalogue/originals and recover interrupted work.

## User controls

**WI-0188 scope (accepted 2026-10-30):** locally available
videos only. Existing optional online-only setting is independently
default-off; testing its transfer/release behavior is deferred to
[WI-0207](../delivery/work-items/WI-0207-opt-in-onedrive-video-hydration-and-release.md).
No online-only originals were hydrated in WI-0188 acceptance.


1. Settings → **Video processing** → **Process videos** (off by default).
2. Optional **Include online-only videos** (off by default).
3. Use the existing **Advance archive** to process all pending photos first
   and then one currently supported, catalogue-inspected video at a time.
   The existing **Pause archive** also cancels active video tool work.
4. Disabling video in Settings fences processing and retains queued work.
   Previously completed video derivative receipts remain revision-bound.
5. Settings shows aggregate video queued/running/succeeded/failed/cancelled
   and expired-lease counts. No video filenames or original paths are exposed.

There is no second start/stop button or photo/video concurrency benchmark.

## Local configuration

The server uses its normal `PhotoIdentity:Postgres:ConnectionString` and
existing maintained PostgreSQL v37 database. This feature introduces **no new
database schema migration**.

Configure already-installed and approved executables with SHA-256. These are
normal appsettings.json keys (environment variables also work); values below
are intentionally **placeholders**, not executable approvals:

~~~json
{
  "PhotoIdentity": {
    "VideoProcessing": {
      "Enabled": false,
      "IncludeOnlineOnly": false,
      "FfmpegPath": "C:\\REPLACE_WITH_APPROVED_FFMPEG\\ffmpeg.exe",
      "FfmpegSha256": "REPLACE_WITH_APPROVED_LOWERCASE_SHA256",
      "FfprobePath": "C:\\REPLACE_WITH_APPROVED_FFPROBE\\ffprobe.exe",
      "FfprobeSha256": "REPLACE_WITH_APPROVED_LOWERCASE_SHA256",
      "CacheRoot": "C:\\REPLACE_WITH_PRIVATE_LOCAL_CACHE"
    }
  }
}
~~~

### Packaged Windows launcher

For the packaged `PhotoIdentity.cmd` / `Start-PhotoIdentity.ps1` workflow,
store the approved tool **paths and lowercase SHA-256 digests** in the
persistent `%LOCALAPPDATA%\\PhotoIdentity\\launcher.json` `settings` object
(after updating to a launcher version that accepts the entries). Keep
`postgresConnectionEnvironmentVariable`, URLs, mobile access and all
existing settings unchanged. Add these four sibling entries:

~~~json
{
  "settings": {
    "PhotoIdentity__VideoProcessing__FfmpegPath": "C:\\APPROVED_TOOLS\\ffmpeg.exe",
    "PhotoIdentity__VideoProcessing__FfmpegSha256": "REPLACE_WITH_ACTUAL_LOWERCASE_SHA256",
    "PhotoIdentity__VideoProcessing__FfprobePath": "C:\\APPROVED_TOOLS\\ffprobe.exe",
    "PhotoIdentity__VideoProcessing__FfprobeSha256": "REPLACE_WITH_ACTUAL_LOWERCASE_SHA256"
  }
}
~~~

The fragment is illustrative; merge the keys into your existing launcher
configuration rather than replacing its contents. For a persistent
non-linked WinGet-safe installation, follow the
[approved pinned FFmpeg and FFprobe runbook](ffmpeg-approved-video-tools.md).
Future CLI verification should load paths and hashes through
Get-PhotoIdentityApprovedVideoTools.ps1, not Get-Command. To calculate approved
digests, use `(Get-FileHash -LiteralPath "C:\\APPROVED_TOOLS\\ffmpeg.exe" -Algorithm SHA256).Hash.ToLowerInvariant()`
(and likewise for FFprobe). Only approve binaries whose provenance you trust.

The launcher checks paths are existing local non-linked `.exe` files, that
digests are lowercase 64-character hex values, and that the four fields
are supplied together. The application **independently recomputes both
file hashes** and verifies cache/source-root isolation at runtime; launcher
validation does not authorize execution.

An optional `PhotoIdentity__VideoProcessing__CacheRoot` may be supplied only
for an already existing private app-owned cache directory, not an archive or
OneDrive root. Normally **omit it** so the application creates its own fixed
app-local cache. Restart the packaged app after configuration changes. The
Settings toggles enable video processing only after the prerequisite tools
have been approved; they do not install FFmpeg.

If the executable configuration was written to `appsettings.Production.json`
inside the published `app` directory, note that a subsequent package update
may overwrite it. Persistent launcher settings are preferred.

The Settings UI stores only the two Boolean choices in
`%LOCALAPPDATA%\PhotoIdentity\video-processing-preferences.json`. If that
file exists, it takes precedence over the appsettings defaults. The UI applies
changes immediately. An invalid saved file fails closed (processing off).

Only an app-owned cache with the exact derivative marker is accepted. A new
cache directory can be created automatically; an existing unmarked directory
is rejected, never claimed as app-owned. The cache must not overlap an archive
source or OneDrive folder. Missing/hash-mismatched tools are refused.

Input support is currently the deliberately narrow WI-0188 SDR 720p
H.264/AAC proxy pilot: local MOV/MP4, H.264 or HEVC Rec.709 with approved
rotation and streams, maximum 600 seconds and 256 MiB input. Unsupported,
large and HDR video are not silently declared successful. More profiles and
codec fallbacks belong to WI-0189 and a possible future Azure design.

**Incremental discovery:** after processing any already-inspected supported
candidates, the shared archive scans the operator's included coverage for
one new MOV/MP4 at a time, preferring already-local files and explicitly
skipping excluded copies. A bounded FFprobe metadata inspection and
SHA-256 verification registers that video as a video-only catalogue
revision. Unsupported inspected clips are not transcoded. With the separate
online-only switch on, the same bounded single-file workflow may request
OneDrive hydration for one previously online-only video and return the
app-initiated download to online-only afterwards. There is no bulk recall,
and the one-file and available disk caps still apply.

## Interpreting video preflight results

The Settings UI accepts four nonempty tool configuration fields before saving
the Process videos preference, but that is **not** executable approval. The
runtime independently checks full local executable paths, strict SHA-256
digests, private cache ownership, absence of directory links and source-root
separation before looking for candidate videos. Previously these distinct
rejections were all shown as `video-safety-or-configuration-rejected`.

The runtime now reports these fixed, path-free stages in **Settings → Video
processing → Last processing result**:

- `video-tool-configuration-rejected`: a tool path/hash is missing or its hash
  does not have the approved lowercase SHA-256 format.
- `video-ffmpeg-approval-rejected`: the configured FFmpeg path, physical file
  or SHA-256 approval is invalid.
- `video-ffprobe-approval-rejected`: the corresponding FFprobe validation
  failed.
- `video-cache-validation-rejected`: the private derivative cache is not
  a verified, physically local, marked, unlinked root separated from archive
  sources.
- `video-safety-or-configuration-rejected`: a different runtime video safety
  gate rejected processing after basic preflight.

Only stage codes are emitted. Neither operator source paths nor executable
paths, digest values, media metadata, exception messages or child-process
output are exposed. A preflight rejection is **not** evidence that previously
indexed photo folders suppress video discovery; video enumeration has not run.

If the published app uses an explicit `PhotoIdentity:VideoProcessing:CacheRoot`,
it must already contain the exact app-owned marker documented above. If the
setting is omitted, the dedicated default app-local cache may be created and
marked automatically. Do not add an ownership marker to an arbitrary directory,
change source locations or disable the guard to resolve a rejection.

## Real-media worker failures after synthetic pilot passes (WI-0188)

The local `video synthetic-proxy-pilot --apply` checks the fixed 720p
encoder/probe contract under Windows constraints using transient generated
inputs. A passing pilot **does not** test actual OneDrive staging,
registered revision hashes, source-specific media, PostgreSQL lease fencing,
real-file I/O limits, or derivative publication.

An operator observed on 2026-10-29: the synthetic landscape/audio and
portrait/no-audio runs both passed (`status=validated`, exit code 0);
the live queue nonetheless had six `failed` jobs with three attempts each,
plus a `queued` and a `running` job, with zero successes.
The live `lastResult=no-admissible-job` is **not** the failure cause:
one reserved running lease can prevent the sole worker from claiming the
queued job. `attempt-limit` is the terminal database reason after three
attempts, not the original low-level worker error.

The settings response now also exposes `lastAttemptFailure` with only
`stage`, `failureClass`, optional `supervisorCheckpoint` and optional
numeric `toolExitCode`. The last failure stays available in memory even
if the next archive pass reports `no-admissible-job`. It resets on
application restart. Earlier attempt causes are not recoverable from
`attempt-limit` rows without one new, explicitly authorized retry.
The API does not return source paths, job input, tool stderr/stdout,
exception messages, or operator configuration.

**After installing this diagnostic build**, while online-only hydration
remains off, inspect the current queue and the retained stage:

```powershell
$status = Invoke-RestMethod "http://127.0.0.1:5080/api/video-processing/settings"
$status | Select-Object enabled, processing, lastResult,
    queued, running, succeeded, failed, expiredLeases, lastAttemptFailure |
    Format-List
$status.recent | Select-Object jobId, status, attempts, progressPercent,
    failureCode | Format-Table -AutoSize
```

If the new build has not seen a failing attempt yet, `lastAttemptFailure`
will be null. Only when the operator chooses one still-current, unexcluded
failed job, use its existing **Retry** action once, then **Advance archive**
to exercise the worker again. Check counters and `lastAttemptFailure`
afterwards; do not mass-retry, reset the PostgreSQL queue, delete the
derivative cache, or bypass any stage guard. A previously running lease may
need to expire and be recovered by the next authorized archive pass before
a new job becomes admissible. `expiredLeases=0` means only that none
were expired at the instant the status was read.

## Shared archive video phase and lease priority — 2026-10-30

The maintainer observed `lastAttemptFailure.stage=lease-safety` and
`failureClass=photo-priority` with `succeeded=0`, seven failed jobs,
and a third-attempt running lease that later expired. This was traced to
a mismatch: the photo-priority *admission* observer recognizes
`runtime_state=video-processing` as the shared archive's own
video phase (only when no sync or photo work remains), but the lease
safety observer previously rejected every `desired_state=running`
archive session. The guarded worker was thus allowed to claim a job
and subsequently vetoed itself.

The fix makes both authoritative photo observers use the same decision:
`sync_required=true` **always** preempts, as does any
`desired_state=running` with `runtime_state` other than
`video-processing`, pending/running photo runs, or associated
queued/running photo jobs. All original/exclusion/lease and publication
guards remain unchanged. A new run may safely recover expired leases
through the existing queue repository; terminal `attempt-limit` rows
are **not** automatically reset.

After installing the fixed build, leave online-only hydration disabled
and start the shared archive once. Observe `running`,
`expiredLeases`, `succeeded`, and `lastAttemptFailure`. If no video
is pending, use the existing **Retry** control for exactly one
eligible failed job (only if the original revision remains current),
start the archive, and inspect the job and any derivative. Do not
repeatedly retry all failed jobs or bypass photo-priority checks.

## First successful published video — operator observation (2026-10-30)

After PR #662, the status API reported `succeeded=1`, `failed=7`,
`queued=0`, `running=0`, `expiredLeases=0`,
`lastResult=no-eligible-video`, and no retained
`lastAttemptFailure`. The succeeded job completed on attempt 1 at
100% progress. This passes the live video-worker durable publication
checkpoint: the PostgreSQL publisher commits the private artifact receipt
and the succeeded job state together. No reset/retry is needed to repeat
this already-proven outcome. The seven terminal `attempt-limit`
rows are historical and need not be cleared.

Do not treat `no-eligible-video` as a failure; it means the current
advance pass found no further eligible input. A blank last failure is
process-local and does not invalidate the historical failed rows.

Before workitem acceptance, verify separately and report: (1) a
previously-local original video still matches its authoritative
catalogue/baseline digest without changing OneDrive hydration, (2) the
private published derivative exists and its bytes match the receipt,
(3) the existing photo Archive, People, searches, collections and
slideshows remain unchanged, and (4) outstanding applicable
pause/cancel, inclusion/exclusion, restart and OneDrive opt-in
acceptance checks. Do not share personal media paths or original hashes
in public PR comments. An already completed single-video run does not
by itself establish all the protective regression gates.

## Maintainer acceptance progress — 2026-10-30

The maintainer confirms **PASS** for restart persistence, original and
published-derivative integrity, and existing photo feature regressions
after the first successful real-video publication. Those checks need
not be repeated solely to close the current checkpoint.

**Further maintained-host PASS (2026-10-30):** Settings controls
and recovery/retry, local-first operation and coverage/exclusion
behavior. All exercised media have been locally available. **No
online-only video hydration or return-to-online-only was exercised.**
Do not interpret successful local-first operation as proof of that
separately opted-in cloud behavior.

**Final targeted safety result:** the maintainer ran the five
isolated Windows test groups below. All **17/17 tests passed** with
**0 failed and 0 skipped**: 8 admission/budget, 2 unsupported codec/
duration/output profile, 4 subprocess failure/cancellation/timeout/
scratch overage, 2 staging integrity/cleanup, and 1 fail-closed
synthetic tool test. These verify disposable-fixture guards and
cleanup; they did not access original family media or cloud data.
An actual online-only hydration and safe online-only release was
**not exercised** and is now [WI-0207](../delivery/work-items/WI-0207-opt-in-onedrive-video-hydration-and-release.md).

## Controlled rejection and temporary-cleanup verification — Windows

This procedure was the final local verification after the maintainer's
PASS for controls/recovery, local-first/exclusion, persistence, integrity
and photo regression. **It passed in full on 2026-10-30 (17/17).** **Do not add fabricated, oversized or damaged videos
to the family archive or a OneDrive folder.** The existing isolated
test fixtures exercise the policy and guard boundaries without opening
archive originals, changing the maintained PostgreSQL queue, invoking
app-driven hydration or publishing any real derivative. These results
are *fixture-based guard evidence*, not proof of a live OneDrive-only
retrieval/release scenario.

From PowerShell in the repository checkout on Windows, run:

```powershell
Set-Location "C:\Kod\codex\Photo Identity Indexer"

function Invoke-VideoSafetyTests {
    param([string]$Project, [string]$Filter)
    dotnet test $Project -c Release --filter $Filter
    if ($LASTEXITCODE -ne 0) {
        throw "WI-0188 isolated safety tests failed: $Project"
    }
}

# Rejection at admission: unsupported inspection, over-budget source,
# and other fail-closed guards. Pure tests; no real source file.
Invoke-VideoSafetyTests ".\tests\PhotoIdentity.Core.Tests\PhotoIdentity.Core.Tests.csproj" `
    "FullyQualifiedName~VideoProcessingAdmissionPolicyTests.Each_explicit_guard_denies_admission"

# Unsupported codec/HDR/rotation/audio and duration > 600 s;
# strict output rejection. Uses in-memory inspection/probe evidence.
Invoke-VideoSafetyTests ".\tests\PhotoIdentity.Integration.Tests\PhotoIdentity.Integration.Tests.csproj" `
    "FullyQualifiedName~VideoProxyCandidateProfileTests.Unrecognized_codec_duration_rotation_and_audio_are_not_admitted|FullyQualifiedName~VideoProxyCandidateProfileTests.Probe_rejects_unsupported_codec_color_tracks_and_dimensions"

# Bounded child process: successful/nonzero exit cleans workspace;
# scratch overage preserves unrelated files; cancel/timeout cleanup.
Invoke-VideoSafetyTests ".\tests\PhotoIdentity.Core.Tests\PhotoIdentity.Core.Tests.csproj" `
    "FullyQualifiedName~VideoToolProcessSupervisorTests.Runs_trusted_command_with_discarded_output_and_cleans_scratch|FullyQualifiedName~VideoToolProcessSupervisorTests.Scratch_overage_fails_and_removes_only_its_temporary_child|FullyQualifiedName~VideoToolProcessSupervisorTests.Cancelled_external_process_is_terminated_and_scratch_cleaned|FullyQualifiedName~VideoToolProcessSupervisorTests.Timed_out_external_process_is_killed_and_cleans_temporary_data"

# Failure during local staging must leave scratch empty and source unchanged;
# disposal of a successful staged copy must also remove the private copy.
Invoke-VideoSafetyTests ".\tests\PhotoIdentity.Source.Tests\PhotoIdentity.Source.Tests.csproj" `
    "FullyQualifiedName~OneDriveVideoLocalStagerTests.Missing_or_changed_hash_and_revoked_permission_leave_no_scratch|FullyQualifiedName~OneDriveVideoLocalStagerTests.Copies_only_local_revision_bytes_and_removes_private_copy"

# Deliberately absent synthetic executable: fail closed, no worker/DB,
# no original opened, only the fixture marker remains in scratch.
Invoke-VideoSafetyTests ".\tests\PhotoIdentity.Integration.Tests\PhotoIdentity.Integration.Tests.csproj" `
    "FullyQualifiedName~VideoSyntheticProxyPilotTests.Missing_external_tool_produces_private_safe_first_stage_failure"
```

**Expected:** all commands exit with code 0 and show nonzero passed
test counts, with zero failures. The admission tests assert
`unsupported-video` and `source-budget` (source size greater
than the configured budget); proxy-profile tests reject an
unsupported codec and a duration of 650 seconds against the
600-second cap. Test assertions check that nonzero exit, timeout,
cancellation and a scratch-budget excess do **not** leave an owned
working directory; unrelated scratch-root content is preserved.
Staging tests assert that hash mismatch or revoked permission leaves
no staged input and that the original fixture's bytes remain intact.
The absent-tool synthetic case asserts `pilot-failed` with no
originals, database or active worker and only its marker in scratch.

**Interpretation:** These tests use a small in-memory source budget to
verify the over-limit decision; they do not allocate a 257 MiB file.
The maintained worker's `MaximumSourceBytes` is 256 MiB, and its
independent inspection cap is 600 seconds. There is no need to create
a giant test file to prove the same admission comparison. Tests
delete only their own randomly named temporary fixtures; do not
manually delete the application's derivative cache, existing
successful proxy, or the seven historical failed jobs.

**Maintainer result (2026-10-30):** all five listed commands passed;
reported totals 8/8, 2/2, 4/4, 2/2 and 1/1; **17 passed,
0 failed, 0 skipped**. No additional invalid-media reproduction is
needed to complete WI-0188's local-file acceptance.
Online-only hydration/release is **NOT EXERCISED** and is separately
tracked by WI-0207; do not hydrate cloud-only media implicitly.

## Local verification session — operator driven

Use only your previously approved local executables and existing v37
catalogue. Do not commit private paths, original digests, or credentials.
Keep a fresh authoritative PostgreSQL backup before testing changes to
production catalogue state.

1. With both video settings off, verify Archive, People, Visual/Caption/
   Combined search, Smart/Creative Collections and slideshow still work.
   Review video queue counters; there must be no new video processing.
2. Enable **Process videos**, leave **Include online-only videos** off,
   and start **Advance archive**. Verify photos drain first. Observe one
   already-local eligible video complete or report a sanitized failure.
   Verify its current revision receipt only after the bounded tool and
   independent FFprobe validator have completed.
3. Verify queued online-only media cannot block a later already-local queued
   video while hydration is disabled. Narrow Archive included folders after
   videos have been queued, then verify the removed folder's queued jobs
   remain unclaimed. The PostgreSQL claim checks current coverage, not merely
   the coverage snapshot when the job was created.
4. While a second video attempts processing, press **Pause archive**.
   The process should stop and leave no published partial output. Start
   archive again to retry/continue from scratch. Repeat after an app restart.
   Do not expect frame-exact resumption.
5. Test a source exclusion or revision change and verify the old video
   receipt is not treated as current; source and photo catalogue records
   must not be changed by the video job. A terminal failed/cancelled job has
   a **Retry** action in Settings which requeues the *same idempotent job*
   from scratch only while the revision remains current, supported and
   unexcluded. A succeeded job or receipt cannot be retried/overwritten.
6. Compare original video SHA-256 before and after a successful run.
   Verify the archive photo files and existing photo functions remain
   unchanged. No video processing step opens originals for writing.
7. **Deferred to WI-0207:** online-only video hydration and
   return-to-online-only release need separate explicit opt-in and
   controlled real-OneDrive verification; they were not exercised
   during WI-0188. Do not enable this setting to repeat WI-0188
   acceptance; never delete an original to reclaim space.
8. Verify a malformed/unsupported/oversized clip never publishes an
   apparently valid result. Failed jobs or observed tool capacity limits
   remain visible for later local troubleshooting or an independently
   reviewed Azure fallback.

The application uses a one-process Windows Job Object and hard CPU/memory,
runtime and scratch policy. These limits can refuse videos that the local
computer cannot process. Such a failure is a diagnostic, not permission to
open cloud workers or copy private originals to Azure.

## Explicit nonrequirements

- No required AppContainer proof; the old disposable synthetic lab remains
  optional and is not used by this ordinary video workflow.
- No concurrent photo/video throughput, search latency or slideshow benchmark.
- No independent video scheduler; shared Archive start/pause is authoritative.
- No arbitrary Internet video input; OneDrive provider-controlled availability
  is the only proposed opt-in remote source.
- No automatic removal of pre-existing local originals. Only app-requested
  hydration can be reverted to OneDrive online-only.
- No automatic Azure upload.

**Final status (2026-10-30): WI-0188 completed / maintainer-accepted
for already-local video processing.** Live publication, restart
persistence, original/derivative integrity, controls/recovery,
local-first/exclusion behavior, photo regressions and all
17/17 isolated invalid-media and owned-scratch tests passed.
The remote-only OneDrive hydration/release path was **not exercised**,
and its opt-in lifecycle is a separately proposed M30 workitem,
[WI-0207](../delivery/work-items/WI-0207-opt-in-onedrive-video-hydration-and-release.md).
The seven historical attempt-limit job rows are untouched.
M30 remains in progress.
