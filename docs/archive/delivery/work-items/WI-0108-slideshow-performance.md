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

- The first WI-0108 slice added aggregate timing for slideshow-library loading, snapshot creation, preparation start/status, prepared-original opening and collection viewer-preview opening to the existing process-local throughput diagnostics.
- `measure-slideshow-performance.ps1` provides a bounded real-catalogue probe and deliberately omits collection names, revision IDs, filenames, source paths and credentials.
- The probe default was corrected from `localhost` to explicit IPv4 loopback after Windows `localhost` resolution introduced an artificial roughly two-second caller delay that was absent from server stages.
- Ordered distinct-image probing showed that direct server latency was bounded and did not grow with slideshow position.
- Browser-side timing instrumentation then measured the actual phone presentation surface and exposed request amplification in the bounded prefetch lifecycle.
- PR #304 stabilized the prefetch set: unchanged URLs retain their `Image` objects, the newly-current resource can reuse/coalesce the previous desired generation, stale entries are removed without restarting the unchanged set, and diagnostics use explicit application-owned prefetch state.

## Maintainer evidence — 2026-09-11

Real-catalogue measurements were run against the accepted PostgreSQL-authoritative production catalogue at schema version 23 after the timing instrumentation was published into the launcher-selected application directory.

For a one-photo saved Smart Collection using `127.0.0.1`:

- saved slideshow-library request: about 9–12 ms caller-observed, with about 8 ms in `slideshow-library-load`;
- snapshot creation: about 23–27 ms caller-observed, with about 21–24 ms in `slideshow-snapshot-creation`;
- first/repeated viewer-preview serving: about 38–41 ms;
- immutable hash verification: about 2.4–2.6 ms per measured read;
- prepared-original opening: about 38–50 ms, with `slideshow-prepared-original-open` averaging about 37 ms;
- preparation start: about 7.5 ms. The roughly 527 ms terminal probe measurement was dominated by the probe's 500 ms status-poll interval rather than synchronous preparation work.

For a representative 11-photo saved Smart Collection using `127.0.0.1`:

- saved slideshow-library request: about 9 ms;
- snapshot creation: about 34 ms caller-observed and about 32 ms in `slideshow-snapshot-creation`;
- first viewer-preview: about 51 ms;
- repeated first viewer-preview: about 34 ms.

These measurements ruled out slideshow-library definition loading, PostgreSQL snapshot creation, immutable hash verification and prepared-original opening as the primary cause of the previously reported multi-second startup delay. No PostgreSQL query/index rewrite or verification-cache weakening was justified by measured cost.

## Ordered-sequence evidence — 2026-09-11

After PR #301 merged, the representative 11-photo collection measured distinct viewer-preview times of approximately 58, 83, 68, 36, 35, 40, 39, 59, 77, 35 and 79 ms. `collection-viewer-preview-open` averaged about 47 ms and peaked around 73 ms.

Five of the 11 distinct images required one `original-open` hash read each. Those reads averaged about 24 ms and peaked around 38 ms. Repeating the first image after the ordered sequence took about 31 ms per request and required no additional original-open hash reads.

The ordered timings fluctuated by image but did not systematically grow with slideshow position. Direct server serving therefore did not reproduce the previously reported progressive slowdown.

## Real-phone browser evidence before prefetch correction — 2026-09-11

After PR #303 merged, the maintainer ran the representative 11-photo slideshow on the phone. The slideshow was subjectively responsive and did not reproduce a user-visible progressive slowdown.

The 11 displayed images averaged about 168 ms browser presentation time and about 156 ms Resource Timing. The same diagnostics generation nevertheless showed preventive request amplification:

- 39 `collection-viewer-preview-open` operations for 11 displayed images;
- 50 `api-collection-request` operations;
- 22 `original-verification-hash` operations;
- 22 `original-open` hash reads across only five revision subjects, with one subject read up to seven times.

Code inspection matched the amplification: every `setPrefetchUrls` call cleared and recreated the complete prefetch `Image` set, and navigation could clear the prefetched resource that had just become current.

## Post-fix phone acceptance — 2026-09-12

After PR #304 merged and the corrected JavaScript was republished/reloaded, the same representative 11-photo phone workflow passed with the slideshow still perceived as responsive.

The corrective result was materially better while the underlying resource-transfer cost stayed essentially unchanged:

- 10 of 11 displayed images were explicit application-owned prefetch hits; only the first image was a miss;
- browser presentation averaged about 7.7 ms overall, with the first image at about 83 ms and images 2–11 at roughly 0.1–0.2 ms after becoming current;
- browser Resource Timing still averaged about 157 ms, confirming the improvement came from completing resource work ahead of presentation rather than making the transfer itself artificially faster;
- `collection-viewer-preview-open` fell from 39 to 12 operations, about a 69% reduction;
- `api-collection-request` fell from 50 to 13 operations, a 74% reduction;
- `original-open` hash reads fell from 22 to 6, about a 73% reduction, across five subjects with at most two reads for any subject;
- there was no systematic latency growth with slideshow position.

This is the intended preventive outcome: bounded prefetch now hides normal phone/network resource latency while avoiding the repeated request/hash amplification that could have become material with larger images, slower networks or weaker clients.

## Acceptance reconciliation

Two original acceptance bullets were phrased as presumed implementation work rather than evidence-based outcomes: rewriting PostgreSQL snapshot query shape and introducing/relying on reusable verification evidence to avoid every full-file hash. The WI-0108 contract itself states that these were hypotheses and that optimizations should be made where they are actual bottlenecks.

The real-catalogue measurements repeatedly showed snapshot creation in the tens of milliseconds and hash verification as non-dominant. Implementing a PostgreSQL rewrite or weakening/complicating immutable verification solely to satisfy those speculative bullets would add risk without addressing a measured performance problem. They are therefore reconciled below to the measured intent: the operations must remain practically bounded, and immutable verification semantics remain unchanged unless future diagnostics demonstrate a material bottleneck.

## Acceptance criteria

- [x] Timing evidence can distinguish slideshow-library load, snapshot creation, preparation/preflight, first-image serving and subsequent-image serving without exposing private source data.
- [x] `/slideshows` lists saved definitions without catalogue query work; measured real-catalogue library loading is about 9–12 ms.
- [x] PostgreSQL slideshow snapshot creation is practically bounded on the real catalogue (about 23–34 ms in the measured one- and 11-photo cases); no speculative query rewrite is justified by current evidence.
- [x] An already-prepared one-photo slideshow does not reproduce the observed long blocking startup path; prepared-original opens measured about 38–50 ms.
- [x] Immutable verification remains safe and measured hash work is non-dominant; no verification cache/skip was introduced solely for performance.
- [x] Any skipped/reused verification remains safe: WI-0108 introduced no weakening of immutable revision verification semantics.
- [x] Image-to-image latency remains bounded through a representative slideshow and does not systematically increase with slideshow position.
- [x] Bounded prefetch caps browser/server resource use without repeatedly restarting unchanged requests; the post-fix phone run produced 10/11 prefetch hits and reduced preview/hash amplification materially.
- [x] Existing M22 fullscreen, protected-mode, preparation, storage-ownership and immutable-snapshot semantics remain unchanged.
- [x] Maintainer verification on the real archive confirms the slideshow library, first-image startup and repeated phone navigation are practically responsive without requiring a SQLite/PostgreSQL comparison run.

## Non-goals

- Changing image quality/fit behavior.
- Weakening immutable revision verification.
- Permanent offline pinning.
- A general browser image CDN.
- Replacing the separate Face Review/Face Gallery/Settings performance work in WI-0104.
