# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**M24 WI-0101, WI-0102 and WI-0103 are completed. The maintainer's production catalogue was cut over to PostgreSQL on 2026-09-11, rollback acceptance passed, and bounded PostgreSQL identity regeneration passed live scale acceptance on 2026-09-11. WI-0104 operator query/UI scaling is in its final Settings closeout slice.**

The production launcher selects PostgreSQL as the single authoritative catalogue. `/health` was maintainer-verified with `status: ok`, `catalogueProvider: postgresql`, PostgreSQL `status: ready` and schema version 23. The preserved final SQLite backup remains a rollback/migration artifact and must not be treated as a second writable authority.

WI-0104 has removed the per-card full people-list rendering and whole-loaded-set reload after a single review action. PR #293 also replaced catalogue-wide latest-review-action page probes with set-based review-state membership and bounded post-page action enrichment. On the same schema-23 catalogue used for the baseline (18,281 faces, 10,366 rank-one suggestions, 8,702 active review actions), the 2026-09-11 after-plan measured 32.397 ms for Suggested person, 19.567 ms for Newest first, 3.973 ms for the all-confidence count and 2.821 ms for the high-confidence count, with no temporary I/O. Face Gallery page/scroll query acceptance is complete and no additional PostgreSQL index is justified by current evidence.

PR #294 completed the face-image slice. Stable 360px Gallery/person-card requests now use a generated-once private response variant derived from the durable 960px contextual face derivative instead of repeating OpenCV resize/re-encode work. Exact-head workflow #1640 passed all required build/test, integration, documentation, review, Windows, launcher and package gates while retaining the `/api/review` `Cache-Control: no-store` privacy boundary.

The active final WI-0104 slice separates Settings loading. `/api/archive/configuration` reads only archive coverage/source identity, and Settings is split into independent Archive Coverage, Archive Storage and Identity Matching components. The shell and archive configuration no longer await full archive status, storage aggregation or review-filter/policy requests. Integration coverage explicitly proves the configuration endpoint still succeeds when the full archive-status repository is replaced by a throwing implementation.

M24's remaining scale work after WI-0104 is WI-0108 (slideshow performance) and, after those dependencies, WI-0106 (PostgreSQL operations and sustained archive catch-up). WI-0105 observability is already completed.

Consolidated real-phone M22 acceptance passed the implemented slideshow behavior except for two functional gaps tracked by WI-0107:

1. **Start slideshow** from `/slideshows` must request fullscreen from the initiating tap/click and continue loading/preparation inside fullscreen without an intermediate application **Enter fullscreen** step when the browser accepts fullscreen.
2. Successful standalone **Prepare originals** state must survive slideshow navigation/page recreation while the exact prepared snapshot remains reusable. The state is a revalidated path-free receipt, not a permanent offline pin.

The same acceptance session found slideshow performance problems. M24 WI-0108 owns slow saved-collection loading, long first-image/startup latency and slow image-to-image transitions; PostgreSQL migration alone is not assumed to fix database-independent repeated file/hash work.

A separate navigation gap observed during PostgreSQL review affects **Collections / Library**, not Smart Collections: opening Photo Details from Collections loses the current result workspace on Back and there is no Previous/Next traversal through that result set. Smart Collection Back-state restoration remains the accepted M19 behavior. Track this as separate navigation follow-up rather than reopening WI-0102.

WI-0076 remains separately recorded as in_progress and is not part of this M24 closeout.

## Next concrete step

For the M24 thread:

1. Verify and merge the final WI-0104 independent Settings-loading slice, then perform a short maintainer Settings smoke before administrative WI-0104 closeout.
2. Complete WI-0108 slideshow-library/start/playback performance work.
3. Then execute WI-0106 operational PostgreSQL backup/recovery and sustained full-archive catch-up acceptance.

For the M22 thread, WI-0107 remains the focused functional closeout for direct originating-gesture fullscreen launch and durable/revalidated prepared-original receipt state.

## Relevant files

- docs/delivery/work-items/WI-0104-operator-query-ui-performance.md
- docs/delivery/work-items/WI-0106-postgresql-operations-and-archive-catchup.md
- docs/delivery/work-items/WI-0108-slideshow-performance.md
- docs/delivery/work-items/WI-0107-m22-slideshow-acceptance-gaps.md
- src/PhotoIdentity.Web/Pages/Settings.razor
- src/PhotoIdentity.Web/Components/ArchiveCoverageSettings.razor
- src/PhotoIdentity.Web/Components/ArchiveStorageSettings.razor
- src/PhotoIdentity.Web/Components/IdentityMatchingSettings.razor
- src/PhotoIdentity.Api/ArchiveStorageEndpoints.cs
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
