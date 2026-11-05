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

**Status: completed — maintainer accepted the bounded opt-in, proxy-derived
poster vertical slice on 2026-11-05.** This is not a claim that the catalogue
has broad poster coverage: 14 of 16 playable videos still lacked registered
posters after the accepted single-video run. Additional posters require
separate operator authorization; browser visits never backfill them.


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

**Longer-term product aspiration (not achieved by this bounded acceptance):** Most already-playable, locally
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

## Acceptance criteria

- [x] A sanitized, current video × playable-proxy ×
  poster baseline identifies the largest gap without
  original-byte access.
- [x] A deliberately authorized, small local-only
  backfill creates a useful, verified and correctly
  oriented still for one representative existing current
  Rec.709 playable proxy **without rereading any original**.
  This acceptance does not independently demonstrate both
  full-range and normal SDR source-profile cases, or broad coverage.
- [x] Cards display the new posters with accessible
  fallbacks, clear media cues, native player controls
  intact, and a discoverable playable-only filter;
  the upper-portion clipping defect was corrected and
  visually verified after PR #742 merged.
- [x] Automated synthetic/disposable PostgreSQL, CLI
  contract and gallery-layout tests check the revised
  profile, source-lock and revision/exclusion fences,
  digest/receipt/canonical-path checks, bounded rendering,
  invalid formats, idempotent selection and retained
  player rendering. CI builds, integration shards,
  launcher and package gates passed. No private original
  or OneDrive content was used in CI or for poster input.
- [x] Maintainer verified a recognizable, correctly oriented
  still and the expected clip's play/pause/seek in the
  maintained Windows browser, plus honest before/after
  aggregates and final full-gallery-image confirmation
  following PR #742. An all-archive batch was not required.

## Delivery guidance

Implement one complete **poster visible in Videos**
vertical slice with automated synthetic/disposable
PostgreSQL safety tests and one operator-controlled
real-media acceptance, avoiding numerous small
PRs or repeated local media tests. WI-0211 was
**proposed** at this stage, not permission to start a backfill
or to read private media. It is now completed only under the
explicit bounded acceptance below. WI-0210 remains completed.

The independent Settings page alignment, sticky
Save/status and cleanup discoverability bug fix
may ship alongside this work-item **proposal**
because it does not process or modify video media.
Its implementation should not be mistaken for
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

**Not completed:** the new versioned, opt-in proxy-to-poster producer,
retained-cache validation and safe JPEG publication/serving, recognizability
and color/rotation acceptance, and real before/after poster population.
Do not mark any whole-work-item acceptance criteria complete or close
WI-0211 based only on this checkpoint.

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

**Pending acceptance:** CI, Windows operator approval of a one-proxy
backfill, human evaluation of visual recognizability, empty/dark-frame
selection, color, rotation, playback continuity and before/after
aggregate counts. Do not close WI-0211 or infer all-catalogue coverage.
The command is never triggered by the browse page or Settings.

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

**Resolved after merge:** PR #742 was merged and the maintainer confirmed
that complete posters now look correct in the gallery. The one-video pilot
is accepted for the explicitly bounded work-item scope; broader poster
coverage remains opt-in and no all-archive batch is implied.

## Final scoped acceptance and completion — 2026-11-05

**WI-0211 completed / accepted by maintainer.** Implementation PRs
[#738](https://github.com/erikwasa/Photo-Identity-Indexer/pull/738),
[#739](https://github.com/erikwasa/Photo-Identity-Indexer/pull/739), and
[#742](https://github.com/erikwasa/Photo-Identity-Indexer/pull/742)
are merged. Their last-head GitHub Actions build runs **41932508414**,
**41936957551**, and **42091266799** respectively completed successfully.

The maintainer ran the focused local proxy-poster CLI integration suite:
**5 passed, 0 failed**; the dry run returned `candidates=1` with no
FFmpeg or original-media reads. With the video lane disabled, paused and
idle, a separately authorized `--max-videos 1 --apply` published one
`wi0211-proxy-still-v1` JPEG: `created=1; skipped=0; failed=0;
originalsRead=0; hydration=0`, exit code 0. Registered posters rose
**1 → 2**; playable without posters fell **15 → 14**; playable video
count remained **16**. The still was recognizable and correctly oriented;
video play, pause, seek and controls remained functional; no unexpected
OneDrive or other background processing was observed. The screenshot also
revealed a cropped gallery thumbnail, fixed by PR #742. **After that PR
merged, the maintainer explicitly confirmed that posters look good in
the gallery view.** This resolves the final visibility acceptance gate.

**Scoped decision and limits:** This closes delivery and human acceptance
of the **operator-controlled proxy-to-poster capability**, its read-only
coverage/filtering and the repaired gallery display, **not** the aspirational
majority-poster population goal. The remaining **14 of 16** playable entries
without a registered still are not silently populated, and no automatic
original access, OneDrive hydration or broad batch is authorized. Rec.709
full-range versus normal SDR variants were not each independently verified
as separate local production examples. These remain optional future
operator-controlled coverage work or narrowly scoped follow-up if a new
issue is observed. Do not re-run private media acceptance or backup/restore
only for documentation closeout. The rest of milestone M30 remains open.
