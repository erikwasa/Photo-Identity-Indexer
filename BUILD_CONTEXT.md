# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**M24 WI-0101 through WI-0105 are completed. WI-0108 slideshow performance is now active; WI-0106 PostgreSQL operations and sustained archive catch-up remains the final substantive M24 work after WI-0108.**

The production launcher selects PostgreSQL as the single authoritative catalogue. `/health` was maintainer-verified with `status: ok`, `catalogueProvider: postgresql`, PostgreSQL `status: ready` and schema version 23. The preserved final SQLite backup remains a rollback/migration artifact and must not be treated as a second writable authority.

WI-0103 passed live PostgreSQL regeneration scale acceptance and WI-0104 passed real-catalogue review/gallery/Settings acceptance. Their canonical administrative closeout merged through PR #296.

The active WI-0108 slice is measurement-only. It adds privacy-safe aggregate timings for slideshow-library loading, snapshot creation, preparation start/status, prepared-original opening and viewer-preview opening to the existing throughput diagnostics. Existing aggregate `original-status` and `original-open` hash-read evidence remains available to quantify repeated full-file immutable verification.

`measure-slideshow-performance.ps1` resets diagnostics between phases and records caller-observed library, snapshot and repeated first-viewer-preview timings without emitting collection names, revision IDs, filenames, paths or credentials. Prepared-original probing is opt-in because it may request hydration and is capped to five snapshot items by default.

Code inspection before the baseline identified two hypotheses to verify on the real PostgreSQL catalogue rather than optimize speculatively: PostgreSQL snapshot creation currently carries the common current-state CTE set and performs final ordering in application memory; local viewer/prepared-original access performs full SHA-256 verification on status/open paths. No query or immutable-verification semantics are changed in this first slice.

Consolidated real-phone M22 acceptance still has two separate functional gaps tracked by WI-0107: direct originating-gesture fullscreen launch and durable/revalidated prepared-original receipt state. Do not mix those functional corrections into WI-0108 performance work.

A separate Collections / Library navigation gap remains outside this M24 thread. WI-0076 also remains separately recorded as in_progress and is not part of the M24 closeout.

## Next concrete step

For the M24 thread:

1. Merge the WI-0108 timing/probe slice after green CI.
2. Run `measure-slideshow-performance.ps1` against a deliberately small representative saved Smart Collection on the real PostgreSQL catalogue; first measure normal viewer-preview, then opt into prepared-original measurement only for a tiny collection.
3. Use the resulting stage/hash evidence to choose the first correction: snapshot query shape, immutable-verification reuse, or another measured stage. Do not add indexes or weaken verification based only on code inspection.
4. Complete WI-0108 real-archive performance acceptance, then execute WI-0106 PostgreSQL startup/restart, backup/restore, sustained catch-up and daily-style increment acceptance.
5. Close M24 only after WI-0108 and WI-0106 are complete and milestone exit criteria are reconciled.

## Relevant files

- docs/delivery/work-items/WI-0108-slideshow-performance.md
- docs/operations/slideshow-performance-diagnostics.md
- measure-slideshow-performance.ps1
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
