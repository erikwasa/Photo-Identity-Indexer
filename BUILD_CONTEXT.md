# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**M24 WI-0101, WI-0102, WI-0103, WI-0104 and WI-0105 are completed. The maintainer's production catalogue was cut over to PostgreSQL on 2026-09-11, rollback acceptance passed, bounded PostgreSQL identity regeneration passed live scale acceptance, and the operator review/gallery/Settings scale work passed real-catalogue acceptance. The next M24 engineering work is WI-0108 slideshow performance, followed by WI-0106 PostgreSQL operations and sustained archive catch-up.**

The production launcher selects PostgreSQL as the single authoritative catalogue. `/health` was maintainer-verified with `status: ok`, `catalogueProvider: postgresql`, PostgreSQL `status: ready` and schema version 23. The preserved final SQLite backup remains a rollback/migration artifact and must not be treated as a second writable authority.

WI-0103 removed the regeneration hot-path multipliers and passed live PostgreSQL scale acceptance on 2026-09-11: 96 eligible targets, 32 confirmed exemplars, bounded eight-target cycles, concurrent status reads within the two-second acceptance bound, durable final counts and zero target/run failures.

WI-0104 removed per-card full people-list rendering and whole-loaded-set reloads, replaced catalogue-wide latest-review-action page probes with set-based state membership and bounded detail enrichment, and added generated-once 360px gallery response variants. On the representative schema-23 catalogue (18,281 faces, 10,366 rank-one suggestions, 8,702 active review actions), the after-plan measured 32.397 ms for Suggested person, 19.567 ms for Newest first, 3.973 ms for the all-confidence count and 2.821 ms for the high-confidence count with no temporary I/O; no additional PostgreSQL index was justified. PR #295 then split Settings into independently loading Archive Coverage, Archive Storage and Identity Matching sections backed by a cheap archive-configuration endpoint. After merge, the maintainer started the normal PostgreSQL-authoritative application and completed the real-catalogue Settings smoke successfully, including independent section loading/refresh and path-safe archive configuration display.

M24's remaining substantive work is WI-0108 (slideshow performance) and, after that dependency, WI-0106 (PostgreSQL operations, backup/restore, sustained archive catch-up and daily-style increment acceptance).

Consolidated real-phone M22 acceptance passed the implemented slideshow behavior except for two functional gaps tracked by WI-0107:

1. **Start slideshow** from `/slideshows` must request fullscreen from the initiating tap/click and continue loading/preparation inside fullscreen without an intermediate application **Enter fullscreen** step when the browser accepts fullscreen.
2. Successful standalone **Prepare originals** state must survive slideshow navigation/page recreation while the exact prepared snapshot remains reusable. The state is a revalidated path-free receipt, not a permanent offline pin.

The same acceptance session found slideshow performance problems. M24 WI-0108 owns slow saved-collection loading, long first-image/startup latency and slow image-to-image transitions; PostgreSQL migration alone is not assumed to fix database-independent repeated file/hash work.

A separate navigation gap observed during PostgreSQL review affects **Collections / Library**, not Smart Collections: opening Photo Details from Collections loses the current result workspace on Back and there is no Previous/Next traversal through that result set. Smart Collection Back-state restoration remains the accepted M19 behavior. Track this as separate navigation follow-up rather than reopening WI-0102.

WI-0076 remains separately recorded as in_progress and is not part of this M24 closeout.

## Next concrete step

For the M24 thread:

1. Start WI-0108 by measuring the PostgreSQL-backed slideshow library, snapshot creation, preparation/preflight, first-image serving and subsequent-image serving paths on the real catalogue.
2. Correct the measured slideshow query/file-verification bottlenecks and complete real-archive maintainer slideshow performance acceptance.
3. Then execute WI-0106 operational PostgreSQL startup/restart, backup/restore and sustained full-archive catch-up acceptance, followed by a small daily-style increment.
4. Close M24 only after WI-0108 and WI-0106 are complete and the milestone exit criteria are reconciled in the canonical status registry.

For the M22 thread, WI-0107 remains the focused functional closeout for direct originating-gesture fullscreen launch and durable/revalidated prepared-original receipt state.

## Relevant files

- docs/delivery/work-items/WI-0108-slideshow-performance.md
- docs/delivery/work-items/WI-0106-postgresql-operations-and-archive-catchup.md
- docs/delivery/work-items/WI-0104-operator-query-ui-performance.md
- docs/delivery/work-items/WI-0103-scalable-match-regeneration.md
- docs/delivery/work-items/WI-0107-m22-slideshow-acceptance-gaps.md
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
