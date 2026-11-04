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

## Acceptance criteria

- [ ] A sanitized, current video × playable-proxy ×
  poster baseline identifies the largest gap without
  original-byte access.
- [ ] A deliberately authorized, small local-only
  backfill creates useful, verified, correctly
  oriented stills for a representative set of
  existing current playable proxies **without
  rereading any original**, including Rec.709
  full-range and normal SDR where present.
- [ ] Cards display the new posters with accessible
  fallbacks, clear media cues, native player controls
  intact, and a discoverable playable-only filter.
- [ ] Tests cover real/dummy proxy boundaries, exact
  versions, sha/ownership/size/geometry, stale
  revisions and exclusions, unsupported formats,
  cancellation, idempotency, no-recall/no-web
  side effects and photo/search regressions.
- [ ] Maintainer verifies recognizable images and
  selecting/playing the expected clip on the
  maintained Windows browser, plus an honest
  before/after aggregate poster count. Do not
  require an all-archive batch to close.

## Delivery guidance

Implement one complete **poster visible in Videos**
vertical slice with automated synthetic/disposable
PostgreSQL safety tests and one operator-controlled
real-media acceptance, avoiding numerous small
PRs or repeated local media tests. WI-0211 is
**proposed**, not permission to start a backfill
or to read private media. WI-0210 remains completed.

The independent Settings page alignment, sticky
Save/status and cleanup discoverability bug fix
may ship alongside this work-item **proposal**
because it does not process or modify video media.
Its implementation should not be mistaken for
poster functionality.
