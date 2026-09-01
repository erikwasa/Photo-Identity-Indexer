---
id: WI-0108
title: Remove slideshow library, startup and playback latency bottlenecks
milestone: M24
status_source: ../status/work-items.yaml
depends_on: [WI-0101]
related_adrs: [ADR-0009]
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres]
---

# WI-0108: Remove slideshow library, startup and playback latency bottlenecks

## Objective

Make the normal slideshow consumption path responsive at archive scale after the authoritative library/slideshow persistence boundary is available on PostgreSQL.

Real-phone M22 acceptance identified three performance problems that must not be assumed to disappear merely because the catalogue moves from SQLite to PostgreSQL:

- loading saved Smart Collections on `/slideshows` takes too long;
- starting a slideshow takes too long, including an already-prepared one-photo slideshow that takes roughly 20 seconds before the image appears and remains slow when reopened immediately;
- loading between slideshow images takes too long and appears to worsen as playback advances.

This work item owns those latency paths explicitly. It does not require comparative SQLite/PostgreSQL benchmark exercises.

## Investigation baseline

Known code paths worth measuring before changing behavior include:

- `/api/slideshows/collections` saved-definition loading;
- Smart Collection slideshow snapshot creation, including catalogue-wide current-state/filter evaluation and in-memory ordering;
- slideshow original-preparation preflight/status checks before first display;
- time to first image response and browser display;
- normal viewer-preview and prepared-original serving;
- repeated immutable SHA-256 verification of an already-local original on every open;
- bounded browser prefetch and whether expensive server work is repeated for prefetched/current images.

These are hypotheses, not predetermined solutions. PostgreSQL query/index improvements should be used where they are the actual bottleneck, but database-independent repeated file/hash/decode work must be corrected separately.

## Contract

- Add low-overhead timing/counter evidence for the major slideshow path stages so delays can be attributed without repeated manual A/B database benchmarking.
- Keep measurements aggregated/path-free; do not log personal filenames, source paths, image content or embeddings.
- Loading the slideshow library should be proportional to the saved definitions needed by that surface and should not execute unnecessary whole-catalogue work.
- Snapshot creation should use PostgreSQL-appropriate indexes/projections/query shapes and avoid repeated whole-catalogue current-state work where a bounded/current-state representation can preserve identical semantics.
- Starting an already-prepared small slideshow must reuse available local/verified state rather than repeat unrelated full preparation or catalogue-scale work.
- Prepared-original and normal slideshow serving must not repeatedly read/hash the entire unchanged original for every display when equivalent immutable verification evidence can be reused safely.
- Any verification cache/receipt must remain tied to the immutable revision and enough observed file identity/state to prevent serving changed bytes as the old revision.
- Prefetch remains bounded and should reduce perceived transition latency rather than multiplying redundant expensive verification.
- Preserve the M22 rule that image loading time is not charged against the configured display duration.

## Acceptance criteria

- [ ] Timing evidence can distinguish slideshow-library load, snapshot creation, preparation/preflight, first-image serving and subsequent-image serving without exposing private source data.
- [ ] `/slideshows` no longer performs unnecessary catalogue-size-dependent work just to list saved Smart Collections.
- [ ] Slideshow snapshot creation on PostgreSQL avoids avoidable repeated whole-catalogue current-state scans while preserving exact saved-collection membership and deterministic order.
- [ ] An already-prepared one-photo slideshow does not repeat the observed long blocking startup path on immediate reopen.
- [ ] Reusing an unchanged prepared original does not require a full-file SHA-256 read on every image request.
- [ ] Any skipped/reused full verification remains safe: changed/unavailable bytes cannot be served as the old immutable revision.
- [ ] Image-to-image latency remains bounded through a representative slideshow and does not systematically increase with slideshow position because of accumulated/repeated work.
- [ ] Bounded prefetch continues to cap browser/server resource use.
- [ ] Existing M22 fullscreen, protected-mode, preparation, storage-ownership and immutable-snapshot semantics remain unchanged.
- [ ] Maintainer verification on the real archive confirms the slideshow library, first-image startup and repeated navigation are practically responsive without requiring a SQLite/PostgreSQL comparison run.

## Non-goals

- Changing image quality/fit behavior.
- Weakening immutable revision verification.
- Permanent offline pinning.
- A general browser image CDN.
- Replacing the separate Face Review/Face Gallery/Settings performance work in WI-0104.
