# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**M24 WI-0101 through WI-0105 are completed. WI-0108 slideshow performance is active; WI-0106 PostgreSQL operations and sustained archive catch-up remains the final substantive M24 work after WI-0108.**

The production launcher selects PostgreSQL as the single authoritative catalogue. `/health` was maintainer-verified with `status: ok`, `catalogueProvider: postgresql`, PostgreSQL `status: ready` and schema version 23. The preserved final SQLite backup remains a rollback/migration artifact and must not be treated as a second writable authority.

WI-0103 passed live PostgreSQL regeneration scale acceptance and WI-0104 passed real-catalogue review/gallery/Settings acceptance. Their canonical administrative closeout merged through PR #296. WI-0108 server timing instrumentation merged through PR #297 and ordered distinct-image probing merged through PR #301.

The 2026-09-11 WI-0108 real-catalogue evidence now rules out the measured server paths as the source of the reported multi-second slideshow delay. With explicit IPv4 loopback, one-photo library/snapshot/viewer/prepared-original operations were tens of milliseconds. The representative 11-photo ordered sequence measured distinct viewer previews between roughly 35 and 83 ms with no systematic growth by slideshow position; `collection-viewer-preview-open` averaged about 47 ms and peaked around 73 ms.

Five of the 11 distinct images required one `original-open` hash read each. Those reads averaged about 24 ms and peaked around 38 ms, but did not accumulate with later positions. Repeating the first preview after the ordered sequence took about 31 ms and required no additional hash read. PostgreSQL query/index work or immutable-verification caching is therefore not justified as the first correction from current evidence.

The earlier `http://localhost:5080` roughly two-second request penalty was a PowerShell/Windows loopback artifact in the diagnostic probe and was corrected by using `http://127.0.0.1:5080`. It does not explain real phone/browser slideshow playback, which uses application-relative URLs.

The active WI-0108 slice instruments the actual browser slideshow surface without changing playback behavior. A browser observer records at most 50 identity-free samples per slideshow: one-based sequence, DOM-presentation-to-`load` elapsed time, same-origin Resource Timing duration when available, and whether the resource had already completed before presentation. Samples are added to the existing process-local throughput diagnostics through `/api/slideshows/diagnostics/playback`.

Consolidated real-phone M22 acceptance still has two separate functional gaps tracked by WI-0107: direct originating-gesture fullscreen launch and durable/revalidated prepared-original receipt state. Do not mix those functional corrections into WI-0108 performance work.

A separate Collections / Library navigation gap remains outside this M24 thread. WI-0076 also remains separately recorded as in_progress and is not part of the M24 closeout.

## Next concrete step

For the M24 thread:

1. Merge the WI-0108 browser-playback diagnostic slice after green CI.
2. Republish the merged `main` build into the launcher-selected application directory.
3. On Windows run `./measure-slideshow-browser-performance.ps1 -Reset`.
4. On the supported phone/browser run the representative slideshow through at least 10 distinct images using the navigation mode that previously felt slow.
5. Back on Windows run `./measure-slideshow-browser-performance.ps1` and inspect per-sequence presentation time, Resource Timing and prefetch hit/miss evidence together with the server stages from the same diagnostics generation.
6. Choose a correction only if the phone evidence identifies a material browser/network/prefetch stage; otherwise move WI-0108 to maintainer performance acceptance.
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
