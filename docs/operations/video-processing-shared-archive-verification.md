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
