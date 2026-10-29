# WI-0188 — photo-first Settings-driven video processing

**Maintainer-revised acceptance (2026-10-22).** This replaces the old
concurrent photo/video benchmark, mandatory AppContainer and comprehensive
network isolation gates. The goal is to process videos after photos safely,
preserve existing photos/catalogue/originals and recover interrupted work.

## User controls

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
configuration rather than replacing its contents. To calculate approved
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
7. Only for an already-catalogued online-only video, explicitly turn on
   **Include online-only videos**, verify one bounded OneDrive hydration
   request and eventual online-only release. If release fails or OneDrive
   is unavailable, the app reports/retains the file; **never delete the
   OneDrive original to reclaim disk**.
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

**Status:** awaiting Windows CI and local end-to-end verification. Do not
mark WI-0188 accepted until the operator reports the results.
