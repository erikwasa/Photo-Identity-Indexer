# WI-0211 — automatic posters for newly processed videos

**Accepted on the maintained Windows host, 2026-11-05.** The
automatic-poster implementation PR #744 merged with passing CI.
The maintainer confirmed that the Settings checkbox works and a
poster was created automatically when processing a new video.
This runbook is retained for future verification, not a request
to process another video solely for work-item closeout.

This is distinct from the earlier manually authorized one-proxy
poster backfill. Future video processing always requires the
established Archive operator authorization.

The new Settings → Video processing checkbox is:

**Automatically create a poster for newly processed videos**

It defaults to **OFF**, including older preferences files. It
persists after Save, but saving never starts Archive or reads media.
When enabled, only a **newly and durably published playable proxy**
during an already operator-started, photo-first Archive advance can
trigger the one JPEG still. The poster step uses the existing
revision-bound verified proxy and pinned FFmpeg/FFprobe tools, not
the original video. It checks for enough private scratch capacity
for a full 256 MiB proxy copy, bounded JPEG and a 512 MiB free-space
reserve; insufficient space is a poster-only failure. Existing poster receipts are skipped. A poster
failure cannot invalidate the successful playable MP4 receipt.

## 1. Update and run synthetic integration tests

Run in PowerShell 7:

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main

dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~VideoProcessingQuotaPreferencesTests|FullyQualifiedName~VideoProcessingSettingsLayoutContractTests|FullyQualifiedName~VideoProxyPosterCommandTests"
if ($LASTEXITCODE -ne 0) { throw "Automatic poster preference tests failed." }

dotnet run --project tools/PhotoIdentity.Docs -- validate
if ($LASTEXITCODE -ne 0) { throw "Documentation validation failed." }
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
if ($LASTEXITCODE -ne 0) { throw "Generated documentation drift." }
~~~

No migration, PostgreSQL backup/restore or original-byte test is
required solely for this Settings and proxy-only continuation change.

## 2. Verify Settings persistence without processing (accepted in practice)

1. Restart the maintained Photo Identity app under its existing
   launcher configuration so the merged code is active.
2. Open Settings → Video processing.
3. Confirm the new automatic-poster checkbox starts OFF if it has
   not previously been set.
4. Check ON and click Save video settings. Refresh and verify ON
   persists without starting Archive or new media jobs.
5. Uncheck and Save. Refresh and verify OFF persists.
6. If you intend to use it for the next already-planned video, check
   ON again and Save.

Saving this setting must not read a video, create JPEGs, hydrate
OneDrive, or trigger any background work.

Optionally inspect a sanitized snapshot using your configured local
API origin:

~~~powershell
$apiBase = "http://127.0.0.1:5080"
$settings = Invoke-RestMethod "$apiBase/api/video-processing/settings"
[pscustomobject]@{
    VideoEnabled = $settings.enabled
    AutomaticPosters = $settings.automaticPostersForNewProxies
    Processing = $settings.processing
    LastPosterResult = $settings.lastPosterResult
}
~~~

## 3. Future newly published video, only when separately authorized

If there is no eligible new proxy awaiting processing, do NOT force
one just to validate the toggle. The automatic path has no backlog
scan. When a normal authorized Archive advance will process a new
eligible video:

1. Keep the existing approved processing profile and source-hydration
   choices. Enable Process videos and this auto-poster setting; Save.
   For bounded acceptance also enable Stop each Archive advance after
   one video step.
2. Before advancing, record read-only counts:

~~~powershell
$before = Invoke-RestMethod "$apiBase/api/videos/coverage"
$before | Select-Object totalVideos,playableVideos,videosWithPosters,playableMissingPosters
~~~

3. Use the normal Advance archive UI action. Do not run manual poster
   backfill. The continuation runs only if one video proxy is actually
   published by this authorized step.
4. Refresh Video processing status after completion. The normal
   Last processing result still reports publication; the separate
   Last automatic poster result is created, skipped,
   skipped-photo-priority or failed. A skipped outcome is valid when a
   registered poster already exists; photo priority defers optional work.
   A failed
   outcome must not undo MP4 publication or playable serving.
5. Compare the read-only counts:

~~~powershell
$after = Invoke-RestMethod "$apiBase/api/videos/coverage"
[pscustomobject]@{
    PostersBefore = $before.videosWithPosters
    PostersAfter = $after.videosWithPosters
    PosterIncrease = $after.videosWithPosters - $before.videosWithPosters
    PlayableBefore = $before.playableVideos
    PlayableAfter = $after.playableVideos
    MissingBefore = $before.playableMissingPosters
    MissingAfter = $after.playableMissingPosters
}
$settings = Invoke-RestMethod "$apiBase/api/video-processing/settings"
[pscustomobject]@{
    LastVideoResult = $settings.lastResult
    LastPosterResult = $settings.lastPosterResult
}
~~~

6. Open Videos and confirm the complete JPEG is recognizable,
   correctly oriented, and not cropped. Open the video and check
   native play, pause and seek. Verify no unexpected original reads,
   OneDrive hydration, photo work or background processing.
7. Disable this setting and Save when no longer needed. Previously
   created posters remain registered and visible.

## 4. Limits

This switch does not populate older proxy posters, cause source
re-reads, approve HDR conversion, alter video proxy encoding, or
create a new queue/worker. Missing poster outcomes do not revoke
successful MP4 receipts. Gallery browsing, search, Refresh and
Settings Save never run FFmpeg. The earlier accepted production
snapshot was two posters and 16 playable videos (14 missing stills);
enabling this switch alone does not change those counts.

Report sanitized aggregate counts and PASS/FAIL. Do not share personal
photos, video frames, paths, job/revision IDs, GPS, hashes or database
credentials in PRs, CI logs or chat.

## Maintainer completion record — 2026-11-05

The complete thumbnail/gallery-framing fix and the earlier manually
authorized one-proxy still were accepted first. The later new-video
automatic option was then merged via PR #744. Its final-head GitHub
Actions [run #3588](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/42100316309)
passed. The maintainer subsequently reported that the automatic-poster
checkbox **works** and a poster was **created when processing a new
video**. This closes the new automatic behavior's maintained-host gate.

The previously measured registered-poster increase **1 → 2**,
playable-without-poster decrease **15 → 14**, and unchanged
**16** playable videos belong to the **earlier manual pilot**.
They are not current coverage numbers after the new-video check.
No new aggregate counts, intentional poster-failure test or
fresh native-player check were supplied for the new video; do
not describe any of those as independently accepted.

The setting continues to default OFF for new/legacy preference files,
does not backfill already published proxies and never causes browsing
or saving Settings to process videos. The poster-only stage is
optional during an authorized Archive run and leaves an already
successful playable proxy unaffected if still production fails.
Do **not** rerun private media processing, backup/restore or a
previous acceptance script merely to close WI-0211.
