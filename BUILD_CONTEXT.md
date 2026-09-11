# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**M24 WI-0101 through WI-0105 are completed. WI-0108 slideshow performance is active; WI-0106 PostgreSQL operations and sustained archive catch-up remains the final substantive M24 work after WI-0108.**

The production launcher selects PostgreSQL as the single authoritative catalogue. `/health` was maintainer-verified with `status: ok`, `catalogueProvider: postgresql`, PostgreSQL `status: ready` and schema version 23. The preserved final SQLite backup remains a rollback/migration artifact and must not be treated as a second writable authority.

WI-0103 passed live PostgreSQL regeneration scale acceptance and WI-0104 passed real-catalogue review/gallery/Settings acceptance. Their canonical administrative closeout merged through PR #296. WI-0108 server timing instrumentation merged through PR #297, ordered distinct-image probing through PR #301, and phone/browser timing instrumentation through PR #303.

The 2026-09-11 WI-0108 evidence rules out the measured PostgreSQL/server paths as the source of the previously reported multi-second delay. The representative 11-photo direct sequence served distinct previews in roughly 35–83 ms with no systematic growth by position. Hash verification was measurable but not cumulative enough to explain the reported slowdown.

The real-phone browser run after PR #303 was also subjectively responsive and showed no progressive slowdown. Eleven displayed images averaged about 168 ms presentation time and about 156 ms Resource Timing. However, the same diagnostics generation recorded 39 `collection-viewer-preview-open` operations, 50 collection API requests and 22 original hash reads. That request amplification is worth correcting preventively even though the current phone experience is acceptable.

Code inspection identified a bounded-prefetch lifecycle defect consistent with the amplification: `setPrefetchUrls` cleared and recreated every browser `Image` object on each refresh, while prefetch is refreshed both around navigation and after image load. At navigation the revision becoming current also leaves the desired prefetch set immediately, so its already-started prefetch could be cleared before the displayed `<img>` finished loading.

The active WI-0108 corrective slice keeps the existing prefetch window and slideshow semantics but stabilizes the browser prefetch set. Unchanged URLs retain their existing `Image` objects, a previous desired generation is retained once so the newly-current image can reuse/coalesce its prefetch, and only stale entries are removed. Browser diagnostics now classify prefetch completion from the slideshow's explicit in-memory prefetch state rather than only the newest Resource Timing entry. No PostgreSQL, image-quality, immutable-verification or prefetch-window-size change is part of this slice.

Consolidated real-phone M22 acceptance still has two separate functional gaps tracked by WI-0107: direct originating-gesture fullscreen launch and durable/revalidated prepared-original receipt state. Do not mix those functional corrections into WI-0108 performance work.

A separate Collections / Library navigation gap remains outside this M24 thread. WI-0076 also remains separately recorded as in_progress and is not part of the M24 closeout.

## Next concrete step

For the M24 thread:

1. Merge the WI-0108 prefetch-deduplication correction after green CI.
2. Republish the merged `main` build into the launcher-selected application directory and reload/reopen the phone client so the bumped slideshow JavaScript versions are active.
3. Reset browser diagnostics with `./measure-slideshow-browser-performance.ps1 -Reset`.
4. Repeat the same representative 11-photo phone run and exit the slideshow so the diagnostic batch flushes.
5. Capture `./measure-slideshow-browser-performance.ps1`, then inspect the same diagnostics generation for `collection-viewer-preview-open`, `api-collection-request`, `original-verification-hash` and hash-read counts.
6. Accept the correction if request/hash amplification drops materially from the 39 preview opens / 22 hash reads baseline, prefetch-hit evidence is credible, latency remains bounded and the already-responsive user experience does not regress.
7. Complete WI-0108, then execute WI-0106 PostgreSQL startup/restart, backup/restore, sustained catch-up and daily-style increment acceptance.
8. Close M24 only after WI-0108 and WI-0106 are complete and milestone exit criteria are reconciled.

## Relevant files

- docs/delivery/work-items/WI-0108-slideshow-performance.md
- docs/operations/slideshow-performance-diagnostics.md
- docs/operations/slideshow-browser-performance-diagnostics.md
- measure-slideshow-performance.ps1
- measure-slideshow-browser-performance.ps1
- src/PhotoIdentity.Web/Pages/Slideshow.razor
- src/PhotoIdentity.Web/Pages/Slideshow.razor.cs
- src/PhotoIdentity.Web/wwwroot/js/slideshow.js
- src/PhotoIdentity.Web/wwwroot/js/slideshow-performance.js
- src/PhotoIdentity.Api/SlideshowOriginalPreparationEndpoints.cs
- src/PhotoIdentity.Api/CollectionViewerPreviewEndpoints.cs
- src/PhotoIdentity.Worker/ArchiveThroughputMetrics.cs
- docs/delivery/work-items/WI-0106-postgresql-operations-and-archive-catchup.md
- docs/delivery/status/work-items.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release

Live PostgreSQL verification:

    ./verify-postgres.ps1
