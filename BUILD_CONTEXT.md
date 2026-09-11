# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**M24 WI-0101, WI-0102 and WI-0103 are completed. The maintainer's production catalogue was cut over to PostgreSQL on 2026-09-11, rollback acceptance passed, and bounded PostgreSQL identity regeneration passed live scale acceptance on 2026-09-11.**

The production launcher selects PostgreSQL as the single authoritative catalogue. `/health` was maintainer-verified with `status: ok`, `catalogueProvider: postgresql`, PostgreSQL `status: ready` and schema version 23. The preserved final SQLite backup remains a rollback/migration artifact and must not be treated as a second writable authority.

WI-0103 now prepares invariant confirmed-exemplar evidence once per durable regeneration run, reads rejection evidence per target, creates the target snapshot with one PostgreSQL `INSERT ... SELECT`, and processes work in bounded eight-target cycles with durable per-target commits. The final isolated live PostgreSQL scale acceptance exercised 96 targets and 32 exemplars while polling status during every active batch; the Release build passed with zero warnings/errors, PostgreSQL persistence tests passed 29/29, and runtime/composition tests passed 7/7 with zero skips.

The PostgreSQL migration/cutover thread also corrected the active identity-regeneration reader lifetime, source-based rehearsal publishing, rehearsal repository-root configuration and a blocking suggestion-gallery query shape. The focused gallery fix made the representative **Needs review** path usable at the maintainer catalogue scale; broader gallery/review query tuning and UI cancellation/responsiveness remain owned by WI-0104.

M24's remaining scale work is now WI-0104 (operator query/UI scaling), WI-0108 (slideshow performance) and, after those dependencies, WI-0106 (PostgreSQL operations and sustained archive catch-up). WI-0105 observability is already completed.

Consolidated real-phone M22 acceptance passed the implemented slideshow behavior except for two functional gaps tracked by WI-0107:

1. **Start slideshow** from `/slideshows` must request fullscreen from the initiating tap/click and continue loading/preparation inside fullscreen without an intermediate application **Enter fullscreen** step when the browser accepts fullscreen.
2. Successful standalone **Prepare originals** state must survive slideshow navigation/page recreation while the exact prepared snapshot remains reusable. The state is a revalidated path-free receipt, not a permanent offline pin.

The same acceptance session found slideshow performance problems. M24 WI-0108 owns slow saved-collection loading, long first-image/startup latency and slow image-to-image transitions; PostgreSQL migration alone is not assumed to fix database-independent repeated file/hash work.

A separate navigation gap observed during PostgreSQL review affects **Collections / Library**, not Smart Collections: opening Photo Details from Collections loses the current result workspace on Back and there is no Previous/Next traversal through that result set. Smart Collection Back-state restoration remains the accepted M19 behavior. Track this as separate navigation follow-up rather than reopening WI-0102.

WI-0076 remains separately recorded as in_progress and is not part of this M24 closeout.

## Next concrete step

For the M24 thread:

1. Start WI-0104, using real PostgreSQL query evidence for remaining review/gallery/operator bottlenecks and adding cancellation/responsiveness where justified.
2. Complete WI-0108 slideshow-library/start/playback performance work.
3. Then execute WI-0106 operational PostgreSQL backup/recovery and sustained full-archive catch-up acceptance.

For the M22 thread, WI-0107 remains the focused functional closeout for direct originating-gesture fullscreen launch and durable/revalidated prepared-original receipt state.

## Relevant files

- docs/delivery/work-items/WI-0103-scalable-match-regeneration.md
- docs/delivery/work-items/WI-0104-operator-query-ui-performance.md
- docs/delivery/work-items/WI-0106-postgresql-operations-and-archive-catchup.md
- docs/delivery/work-items/WI-0108-slideshow-performance.md
- docs/delivery/work-items/WI-0107-m22-slideshow-acceptance-gaps.md
- docs/delivery/milestones/M24-postgresql-catalogue-and-scale.md
- docs/architecture/postgresql-runtime-composition.md
- docs/operations/postgresql-catalogue-cutover.md
- docs/operations/postgresql-local-runtime.md
- src/PhotoIdentity.Persistence.Postgres/PostgresIdentityMatchRegenerationRepository.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresIdentityMatchRegenerationScorer.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresSuggestionGalleryRepository.cs
- src/PhotoIdentity.Api/Program.cs
- docs/delivery/status/work-items.yaml
- docs/delivery/status/milestones.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release

Live PostgreSQL verification:

    ./verify-postgres.ps1
