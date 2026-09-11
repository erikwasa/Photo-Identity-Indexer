# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**M24 WI-0101 through WI-0105 are completed. WI-0108 slideshow performance is active; WI-0106 PostgreSQL operations and sustained archive catch-up remains the final substantive M24 work after WI-0108.**

The production launcher selects PostgreSQL as the single authoritative catalogue. `/health` was maintainer-verified with `status: ok`, `catalogueProvider: postgresql`, PostgreSQL `status: ready` and schema version 23. The preserved final SQLite backup remains a rollback/migration artifact and must not be treated as a second writable authority.

WI-0103 passed live PostgreSQL regeneration scale acceptance and WI-0104 passed real-catalogue review/gallery/Settings acceptance. Their canonical administrative closeout merged through PR #296. The first WI-0108 timing/probe slice merged through PR #297.

The 2026-09-11 WI-0108 real-catalogue baseline materially narrows the performance investigation. Once the PR #297 build was actually published into the launcher-selected application directory and the probe used `http://127.0.0.1:5080`, the measured server paths were already fast: one-photo library/snapshot/viewer/prepared-original operations were tens of milliseconds, and an 11-photo representative collection created its snapshot in about 34 ms and served its first preview in about 51 ms. Hash verification in the measured one-photo path was only a few milliseconds per read.

The probe's original `http://localhost:5080` default produced an artificial roughly two-second delay per request on the maintainer's Windows environment while server stages stayed fast. That was a probe/client loopback artifact and does not explain real browser slideshow playback, which uses application-relative URLs.

The evidence therefore does not justify a PostgreSQL query/index rewrite or immutable-verification cache as the first WI-0108 correction. The remaining direct-server evidence gap is progression through distinct slideshow items: the first probe repeatedly fetched only the first revision and could not test the reported latency growth as playback advances.

The current slice changes the probe default to explicit IPv4 loopback and adds a bounded ordered distinct-viewer-preview sequence. It preserves repeated-first-preview and optional prepared-original phases for cache/hash evidence and keeps reports path-free and revision-free.

Consolidated real-phone M22 acceptance still has two separate functional gaps tracked by WI-0107: direct originating-gesture fullscreen launch and durable/revalidated prepared-original receipt state. Do not mix those functional corrections into WI-0108 performance work.

A separate Collections / Library navigation gap remains outside this M24 thread. WI-0076 also remains separately recorded as in_progress and is not part of the M24 closeout.

## Next concrete step

For the M24 thread:

1. Merge the WI-0108 ordered-sequence diagnostic slice after green CI.
2. Republish the merged `main` build into the launcher-selected application directory.
3. Run `measure-slideshow-performance.ps1` against the representative 11-photo Smart Collection with the default ordered sample (or `-SequenceItemCount 11`) and inspect `sequenceViewerPreviews`, `sequenceViewerPreviewStages` and hash-read evidence.
4. If distinct direct-server image latency remains bounded with slideshow position, instrument the actual slideshow playback surface for browser-visible image request/load/prefetch timing and reproduce on the real phone before changing PostgreSQL query shape or immutable verification semantics.
5. Complete WI-0108 real-archive performance acceptance, then execute WI-0106 PostgreSQL startup/restart, backup/restore, sustained catch-up and daily-style increment acceptance.
6. Close M24 only after WI-0108 and WI-0106 are complete and milestone exit criteria are reconciled.

## Relevant files

- docs/delivery/work-items/WI-0108-slideshow-performance.md
- docs/operations/slideshow-performance-diagnostics.md
- measure-slideshow-performance.ps1
- src/PhotoIdentity.Web/Pages/Slideshow.razor
- src/PhotoIdentity.Web/Pages/Slideshow.razor.cs
- src/PhotoIdentity.Api/SmartCollectionEndpoints.cs
- src/PhotoIdentity.Api/SlideshowOriginalPreparationEndpoints.cs
- src/PhotoIdentity.Api/CollectionViewerPreviewEndpoints.cs
- src/PhotoIdentity.Api/CollectionOriginalAccessService.cs
- src/PhotoIdentity.Api/SlideshowOriginalPreparationService.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresSmartCollectionQueryRepository.cs
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
