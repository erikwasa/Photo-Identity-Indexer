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

## Implementation progress

- The first WI-0108 slice is measurement-only: it adds aggregate stages for slideshow-library loading, snapshot creation, preparation start/status, prepared-original opening and collection viewer-preview opening to the existing process-local throughput diagnostics. Existing `original-status` and `original-open` hash-read aggregates remain the evidence for repeated full-file immutable verification.
- `measure-slideshow-performance.ps1` provides a bounded real-catalogue probe. It resets diagnostics between phases, measures library and selected snapshot latency, downloads the same first viewer-preview repeatedly, and can optionally exercise prepared-original serving only when explicitly enabled. Prepared-original probing refuses collections above a caller-visible item cap by default so the diagnostic does not accidentally hydrate a large slideshow.
- The probe/report deliberately omits collection names, revision IDs, filenames, source paths and credentials. It records only catalogue provider/schema, item counts, wall-clock timings, aggregate stage timings and aggregate hash-read statistics.
- Code inspection before optimization confirmed two hypotheses that the real-catalogue probe could distinguish: PostgreSQL snapshot creation currently carries the common current-state CTE set even when a saved filter does not require every state domain and sorts the full candidate set in application memory; local viewer/prepared-original paths can perform full SHA-256 verification on status/open. Neither should be optimized without measured evidence that it is material.
- The follow-up measurement slice changes the probe's default loopback origin to `http://127.0.0.1:5080` and adds bounded ordered viewer-preview measurements across distinct slideshow positions. This closes the direct-server evidence gap for the acceptance criterion that transition latency must not increase as playback advances.

## Maintainer evidence — 2026-09-11

Real-catalogue measurements were run against the accepted PostgreSQL-authoritative production catalogue at schema version 23 after PR #297 was republished into the launcher-selected application directory.

The first probe runs used the script's original `http://localhost:5080` default. Every caller-observed request incurred roughly two seconds while the corresponding server stages remained in the tens of milliseconds. Re-running the same measurements against `http://127.0.0.1:5080` removed that delay completely. The delay was therefore a probe/client loopback artifact on the maintainer's Windows environment, not slideshow-library, snapshot, file-open or hashing work. The probe default must follow the launcher-style explicit IPv4 loopback address.

For a one-photo saved Smart Collection using `127.0.0.1`:

- saved slideshow-library request: about 9–12 ms caller-observed, with about 8 ms in `slideshow-library-load`;
- snapshot creation: about 23–27 ms caller-observed, with about 21–24 ms in `slideshow-snapshot-creation`;
- first viewer-preview: about 38–41 ms, with collection viewer-preview open averaging about 34–35 ms;
- repeated viewer-preview: about 38–40 ms;
- immutable hash verification: about 2.4–2.6 ms per measured read;
- prepared-original opening: about 38–50 ms, with `slideshow-prepared-original-open` averaging about 37 ms;
- preparation start: about 7.5 ms, and the roughly 527 ms terminal measurement is dominated by the probe's 500 ms status-poll interval rather than synchronous preparation work.

For a representative 11-photo saved Smart Collection using `127.0.0.1`:

- saved slideshow-library request: about 9 ms;
- snapshot creation: about 34 ms caller-observed and about 32 ms in `slideshow-snapshot-creation`;
- first viewer-preview: about 51 ms;
- repeated first viewer-preview: about 34 ms;
- collection viewer-preview open averaged about 36 ms;
- no original-open hash reads were observed for that viewer-preview sequence.

These measurements rule out slideshow-library definition loading, PostgreSQL snapshot creation, immutable hash verification and prepared-original opening as the primary cause of the previously reported multi-second one-photo startup delay for the measured collections. No PostgreSQL index/query rewrite or verification-cache weakening is justified from this evidence.

The remaining evidence gap is actual playback progression. The first probe repeatedly requested only the first snapshot revision and therefore could not test whether distinct image transitions become slower with slideshow position. The next probe slice measures a bounded ordered sequence of distinct viewer previews while retaining the repeated-first-image phase for cache/verification evidence. If ordered direct-server latency remains bounded, the investigation should move to browser-visible request/render/prefetch timing on the real playback surface rather than speculative server optimization.

## Ordered-sequence evidence — 2026-09-11

After PR #301 merged, the representative 11-photo collection was measured with the bounded ordered probe. Caller-observed viewer-preview times by position were approximately 58, 83, 68, 36, 35, 40, 39, 59, 77, 35 and 79 ms. `collection-viewer-preview-open` averaged about 47 ms and peaked around 73 ms.

Five of the 11 distinct images required one `original-open` hash read each. Those reads averaged about 24 ms and peaked around 38 ms. Repeating the first image after the ordered sequence took about 31 ms per request and required no additional original-open hash reads.

The ordered timings fluctuate by image but do not systematically grow with slideshow position: several later positions return to the mid-30-to-40 ms range. Direct server serving therefore does not reproduce the reported progressive slowdown, and the measured hash work is not cumulative enough to explain it.

The next WI-0108 slice instruments the actual browser slideshow image surface. It records at most 50 identity-free samples per slideshow: one-based sequence, time from DOM presentation to `<img>` load, same-origin Resource Timing duration when available, and whether the resource had already completed before presentation. Samples are batched so diagnostics do not compete with every image prefetch. No image URL, revision ID, collection identity, filename or source path is submitted.

## Acceptance criteria

- [x] Timing evidence can distinguish slideshow-library load, snapshot creation, preparation/preflight, first-image serving and subsequent-image serving without exposing private source data.
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
