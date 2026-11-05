---
id: WI-0211
title: Make video browsing recognizable with verified posters
milestone: M30
status_source: PhotoIdentity.Docs
depends_on: [WI-0151, WI-0152, WI-0189, WI-0210]
related_adrs: [ADR-0012, ADR-0013]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Cli, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Web, docs]
---

# WI-0211: Make video browsing recognizable with verified posters

**Completion: maintainer accepted 2026-11-05, scoped to the verified
poster capability and future-video automation.** This does not mean most
catalogue videos have already received posters. Existing gaps require
explicitly approved backfill; the automatic setting applies only to
newly published proxies.

## Problem and user outcome

The maintainer's 2026-11-04 review of the **Videos** page showed
many near-identical black `Video` tiles. It is hard to choose a
clip to play without recognizing its visual content. A
`PlayableProxy` receipt and a durable `video_still_posters` image
are **different independently versioned derivatives**.
Completing WI-0210's one-video production Rec.709 playback
acceptance did not populate missing posters.

WI-0151 already delivered the safely bounded, revision-bound
**manual/local SDR video-poster producer**, its PostgreSQL
persistence, poster serving and exclusions; it is **completed**.
WI-0189 and WI-0210 deliver playable proxies. None of those
work items committed to ensuring that the ordinary Videos
library has useful poster coverage for the actual catalogue.
This is a **new product coverage gap**, not grounds to reopen
the accepted technical scopes.

**User-visible goal:** Most already-playable, locally
registered videos should show an identifiable, correctly
oriented still image instead of an undifferentiated
placeholder; users can distinguish and open the intended
video without first playing many others. Unsupported,
excluded or online-only originals never become implicitly
readable merely because the page is opened.

## Bounded first implementation slice

1. **Read-only baseline.** Count current nonexcluded videos,
   `HasPlayableProxy`, `HasPoster`, intersections, missing
   stills among already-playable proxies, and known metadata
   exclusions. Avoid disclosing filenames, paths, GPS,
   revision IDs, frame images or personal content into
   CI, logs, PRs or chat. The earlier census found only
   one registered still poster among 58 current videos;
   that is *historical*, not a new live count.
2. **Prioritize existing retained playable proxies.** Supply
   a separately approved, bounded, idempotent poster
   backfill from the **already-published local proxy
   cache**, not the original. Validate the exact
   source/revision/exclusion, cache ownership, receipt,
   hash, media type and length under existing lock
   protection. Require approved, digest-pinned FFmpeg,
   process/time/output limits and a versioned
   crop-free JPEG poster profile. Sample deterministic
   scene positions to avoid blank/opening frames where
   practical. Correct portrait orientation and output
   colour must be independently verified; do not claim
   general-purpose HDR-to-SDR support. Never decode a
   random on-disk MP4, trust a user-supplied path or
   copy embedded GPS/metadata to the still.
3. **Safe activation and future delivery.** An operator
   explicitly chooses a small, count-bounded local
   backfill. Merely visiting `/videos`, loading images,
   searching, saving settings or opening the player
   must **never** trigger original reads, OneDrive
   hydration, inference or video transcode. New
   full-range/standard/compact proxies can generate
   poster evidence only as part of an already
   operator-authorized publication step, using the
   same reviewed containment and current-revision
   fences. Do not make every old proxy regenerate
   by default.
4. **Library clarity.** Display the still within each
   existing card with its aspect ratio respected,
   preserving the native player sizing from WI-0152.
   Offer a simple playable-only view and clear
   duration/date/resolution cues. Missing posters
   stay honestly labeled; do not display a
   frame from a different revision, assume a
   poster from arbitrary private source paths,
   surface sensitive camera/location metadata or
   hide unsupported catalogued videos by default.
5. **Quality and reversibility.** Use the existing
   dedicated poster cache and revision-bound
   safe serving. Verify no dangling receipts,
   symlink/path escapes, stale/excluded poster
   delivery, incorrect orientation, black-frame
   acceptance, photo query regression, unauthorized
   original reads, or accidental replacement
   of existing manual verified posters.
   An operator can stop after a bounded batch.
   Existing registered playable proxies remain
   playable even if poster extraction fails.

## Out of scope

- Automatically opening local originals for every
  video without separate authorization.
- Hydrating OneDrive-only sources, especially to fill
  thumbnails for entries without playable proxies;
  WI-0207 owns opt-in managed hydration.
- Tone-mapping unknown HLG/PQ originals, scene
  caption embeddings, face detection, video search
  expansion, universal containers/codecs and
  metadata-derived private filenames.
- Reprocessing already published videos or requiring
  all catalogue videos to have a poster before
  viewers can work.
- Making the Videos browse endpoint perform
  FFmpeg/inference or create database writes.

## Acceptance criteria — final scoped result

- [x] A sanitized, current video × playable-proxy × poster coverage
  baseline is available without original-byte access, including a
  playable-only gallery filter. Registered evidence is counted honestly.
- [x] The persisted **Automatically create a poster for newly processed
  videos** Settings checkbox defaults **OFF** and controls optional
  automatic poster extraction only after the existing operator-started
  Archive phase durably publishes a **new** playable proxy.
  The maintainer confirmed the checkbox works and a new video
  automatically received a poster while being processed (PR #744).
  Saving/browsing do not trigger media work; older proxies are not
  backfilled by the switch.
- [x] A separate approved, small, manual proxy-only backfill generated
  one verified and correctly oriented representative JPEG without
  rereading an original. This is a bounded single-video acceptance,
  **not** independent proof for every Rec.709 range/codec variant.
- [x] The gallery shows complete posters without cropping, provides
  appropriate missing-poster fallbacks and playable-only filtering,
  and preserves the native player. The maintainer confirmed full
  gallery posters look good after PR #742; play, pause and seek
  passed on the earlier representative video.
- [x] Synthetic, disposable PostgreSQL and UI/CLI integration tests
  cover opt-in persistence, current/exclusion fences, canonical
  poster/proxy paths, hash/size/format, bounded output, rejection
  of unsupported/dark images, duplicate selection, source locks,
  and no gallery-triggered processing. Final-head CI passed for
  all four implementation PRs. The new auto-poster continuation
  preserves successful proxy publication if a poster fails;
  no deliberately induced production failure was required.
- [x] Maintained Windows acceptance: earlier one-video proxy-to-poster
  pilot and before/after aggregate coverage passed; the repaired
  full-image gallery was visually accepted; after the automatic
  setting merged, the maintainer confirmed the checkbox functions
  and a poster is created when processing a new video. No new
  numeric coverage snapshot was supplied for that last check,
  so none is inferred.

## Delivery guidance

Implement one complete **poster visible in Videos**
vertical slice with automated synthetic/disposable
PostgreSQL safety tests and one operator-controlled
real-media acceptance, avoiding numerous small
PRs or repeated local media tests. WI-0211 began as **proposed**; this is historical delivery
guidance, not permission to start further private-media
backfills. WI-0210 remains completed.

The independent Settings page alignment, sticky
Save/status and cleanup discoverability bug fix
was a separate visual cleanup and did not itself process
or modify media; its implementation was not treated as
poster functionality.

## Implementation checkpoint 1 — read-only coverage and usable filtering (2026-11-05)

The first scoped change adds **read-only**, current-revision source/exclusion-
filtered aggregate poster/proxy counts and a real server-side paginated
`Playable videos only` control on `/videos`. It retains the existing
native player and revision-bound poster service and improves the missing-
poster label. Counts report *registered evidence*, not successful media
file validation; the playback and poster endpoints still perform their
own guarded byte verification. All new functionality is metadata-only
and does not request original bytes, OneDrive hydration, transcoding,
FFmpeg, publishing or background processing.

Disposable PostgreSQL regression fixtures cover current, excluded and
superseded revisions and real registered proxy receipts. A separate source
contract protects the browse filter, native controls and read-only route.
After merge, the optional maintained-browser and sanitized aggregate
verification is documented in
[`video-poster-coverage-wi0211.md`](../../operations/video-poster-coverage-wi0211.md).

**Historical checkpoint 1:** the producer and visual acceptance were
not yet implemented at this stage; later PRs and maintainer checks
completed the bounded vertical slice. Do not treat this first
checkpoint alone as full acceptance.

## Implementation checkpoint 2 — opt-in poster from a retained proxy (2026-11-05)

A dedicated, dry-run-first CLI now selects **at most three** current,
visible, nonexcluded video revisions with a registered supported
PlayableProxy but no recognized poster. Apply mode uses the existing
revision-fenced, digest-verified MP4 playback lease; that same PostgreSQL
source lock stays held across copying the proxy to isolated scratch,
independent pinned FFprobe inspection, bounded pinned FFmpeg JPEG rendering,
output inspection and immutable still-poster SQL registration.

Profile `wi0211-proxy-still-v1` uses a deterministic partial-clip
representative-frame extraction and crop-free 960px maximum JPEG. It
refuses unapproved FFmpeg/FFprobe hashes, absent/overlapping or linked
cache/poster roots, unsupported proxy color/format/rotation, stale or
excluded revisions, extra originals, and any duplicate existing poster.
Existing original-backed WI-0151 posters take precedence. Poster serving
checks this new profile under the established current-revision, exclusion,
canonical-path, file signature and checksum guard. Missing or corrupt
registered stills still have an honest UI fallback; playable proxies remain
playable if still extraction fails.

New disposable PostgreSQL tests exercise source-locked synthetic poster
publication, saved-file delivery, existing proxy compatibility, exclusion,
and current-revision fences. Opt-in argument/pinning and rendering-recipe
contracts are covered by CLI tests. No private media is used in CI.
The maintained-host procedure and exact PowerShell commands are in
[`video-proxy-posters-wi0211-acceptance.md`](../../operations/video-proxy-posters-wi0211-acceptance.md).

**Historical checkpoint 2:** later CI and maintained-host one-proxy
acceptance passed, as recorded below. The command is never
triggered by the browse page or Settings; all-catalogue coverage
was neither requested nor implied.

## Maintained Windows acceptance and gallery framing correction (2026-11-05)

The maintainer verified the proxy-derived poster vertical slice against an
approved local, already-retained playable proxy. The focused CLI integration
suite passed (5 of 5). The metadata-only preview reported one candidate,
the single authorized apply published exactly one still with exit code zero,
and the video lane remained disabled/paused and idle. Registered poster
coverage increased from **1 to 2** for **16** existing playable videos;
playable videos lacking posters decreased from **15 to 14**. The maintainer
confirmed the poster was recognizable, correctly oriented, and that the
related native video player still played, paused and sought normally. No
unexpected OneDrive or other processing was observed.

Visual review also identified an **additional gallery-only clipping bug**:
the JPEG thumbnail element could retain an intrinsic height inside its
fixed-aspect grid stage, so `overflow: hidden` showed only the upper portion
of an otherwise valid still. The targeted fix absolutely positions the
`img` at `inset: 0` inside the existing relative `16:10` stage and uses
`object-fit: contain` and centered positioning. The resulting complete
JPEG is letterboxed as needed rather than stretched or cropped. The
expanded native video player layout, original/published media, source
permissions and poster publication logic are unchanged. A CSS/layout
contract test guards against reintroducing the failure.

**Gallery gate resolved:** after PR #742 merged, the maintainer confirmed
that full posters now look good in the video gallery. WI-0211 remains
open for the newly requested automatic-poster option documented below;
broader poster coverage remains opt-in and no all-archive batch is implied.

## Implementation checkpoint 3 — optional automatically created posters (2026-11-05)

The maintainer confirmed that the **entire poster looks good** in the
gallery after merged PR #742. The first opt-in one-proxy poster flow
and browse experience were already accepted. Before the separate
administrative closeout PR #743 merged, the maintainer identified
one important additional requirement: a newly processed video
should optionally receive a poster **without running a second CLI**.

This checkpoint extends the same work item with a persistent, explicit
`AutomaticPostersForNewProxies` setting in Settings → Video processing.
It defaults to **off**, including when loading an older preferences JSON.
Save updates the active preference without restarting the application
and never starts processing. The setting is independent of whether
the existing video processing lane is enabled.

Only the existing **operator-started photo-first Archive advance** may
execute the continuation, and **only after** its video worker has
verified a genuinely successful durable `PlayableProxy` publication.
The continuation revalidates the source-locked playback lease,
approved pinned FFmpeg/FFprobe, cache and poster roots, and output.
A registered WI-0151/WI-0211 poster is skipped without rerendering.
A new photo-priority signal defers this optional stage; private
scratch-capacity admission preserves a 512 MiB reserve beyond the
worst-case 256 MiB proxy copy and bounded JPEG. Other video revisions,
search/viewer requests and background idle tasks are never backfilled. Any failure is reported as a sanitized
poster-only outcome and does **not** invalidate the already-published
MP4. The total continuation is separately bounded to 90 seconds.
Existing local-only and optional online-only video source policy
is unchanged; poster extraction itself reads only the proxy.

**Verification resolved:** PR #744 merged, final-head CI passed, and
the maintainer confirmed the Settings checkbox works and an automatically
created poster appeared while processing a new video. The bounded
Windows procedure remains in
[`video-automatic-posters-wi0211-acceptance.md`](../../operations/video-automatic-posters-wi0211-acceptance.md).
The pre-auto closeout draft PR #743 was superseded by the final
post-acceptance closeout.

## Final acceptance and closeout — 2026-11-05

**WI-0211 completed for the explicitly scoped functional outcome.**
Merged implementation PRs
[#738](https://github.com/erikwasa/Photo-Identity-Indexer/pull/738)
(read-only coverage and filtering),
[#739](https://github.com/erikwasa/Photo-Identity-Indexer/pull/739)
(approved bounded proxy-derived poster creation),
[#742](https://github.com/erikwasa/Photo-Identity-Indexer/pull/742)
(complete thumbnail framing) and
[#744](https://github.com/erikwasa/Photo-Identity-Indexer/pull/744)
(default-off automatic new-proxy poster setting) all passed their
final-head GitHub Actions checks; the auto-poster run is
[#3588](https://github.com/erikwasa/Photo-Identity-Indexer/actions/runs/42100316309).

**Earlier measured Windows acceptance:** selected integration tests
5 passed/0 failed; metadata preview found 1 candidate; a separately
authorized single-proxy poster operation returned `created=1`,
`skipped=0`, `failed=0`, `originalsRead=0`, `hydration=0`, exit 0.
Registered posters increased **1 → 2**, playable videos missing
posters decreased **15 → 14**, and playable videos stayed **16**.
The maintained-browser poster was recognizable/correctly oriented;
the native video played, paused and sought; no unexpected OneDrive
or other activity was observed. The maintainer later confirmed that
the corrected gallery displays complete posters.

**New automatic-poster acceptance:** after PR #744 merged, the
maintainer explicitly reported **"Checkbox works. Poster created
when processing new video."** This confirms the requested practical
new-video behavior. No new before/after aggregate counts, separate
new-video player test, or deliberate poster-failure scenario were
reported; such results must not be invented. The code and tests
enforce the independent poster-only failure behavior.

**Limits kept after completion:** The automatic switch is **OFF
by default**, is saved through existing video preferences, and
only operates after a newly successful, operator-authorized
playable-proxy publication; gallery viewing/Settings Save never
starts it. Existing playable videos lacking posters are not
automatically populated. The prior known **14 missing posters
among 16 playable videos** is a historical snapshot from the
manual acceptance and is **not** asserted to be current after
the newly processed video. Unsupported/HDR sources and universal
Rec.709 variants are not claimed supported by this one-video
acceptance. M30 stays open for its other work items.
