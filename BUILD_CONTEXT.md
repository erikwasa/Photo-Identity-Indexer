# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**M24 WI-0101, WI-0102 and WI-0103 are completed. The maintainer's production catalogue was cut over to PostgreSQL on 2026-09-11, rollback acceptance passed, and bounded PostgreSQL identity regeneration passed live scale acceptance on 2026-09-11. WI-0104 operator query/UI scaling is active.**

The production launcher selects PostgreSQL as the single authoritative catalogue. `/health` was maintainer-verified with `status: ok`, `catalogueProvider: postgresql`, PostgreSQL `status: ready` and schema version 23. The preserved final SQLite backup remains a rollback/migration artifact and must not be treated as a second writable authority.

WI-0104 has removed the per-card full people-list rendering and whole-loaded-set reload after a single review action. PR #293 also replaced catalogue-wide latest-review-action page probes with set-based review-state membership and bounded post-page action enrichment. On the same schema-23 catalogue used for the baseline (18,281 faces, 10,366 rank-one suggestions, 8,702 active review actions), the 2026-09-11 after-plan measured 32.397 ms for Suggested person, 19.567 ms for Newest first, 3.973 ms for the all-confidence count and 2.821 ms for the high-confidence count, with no temporary I/O. Face Gallery page/scroll query acceptance is therefore complete and no additional PostgreSQL index is justified by current evidence.

The active WI-0104 slice now targets review imagery. A durable 960px contextual face derivative already exists and Details serves it directly, but 360px Gallery/person-card requests were reopening and OpenCV-resizing/re-encoding that stable JPEG on every request. The current branch adds a private generated-once 360px response cache beside the durable derivative while preserving the established `/api/review` `Cache-Control: no-store` privacy boundary. After this image slice, the remaining WI-0104 acceptance area is independent Settings configuration/shell loading.

M24's remaining scale work is WI-0104 (operator query/UI scaling), WI-0108 (slideshow performance) and, after those dependencies, WI-0106 (PostgreSQL operations and sustained archive catch-up). WI-0105 observability is already completed.

Consolidated real-phone M22 acceptance passed the implemented slideshow behavior except for two functional gaps tracked by WI-0107:

1. **Start slideshow** from `/slideshows` must request fullscreen from the initiating tap/click and continue loading/preparation inside fullscreen without an intermediate application **Enter fullscreen** step when the browser accepts fullscreen.
2. Successful standalone **Prepare originals** state must survive slideshow navigation/page recreation while the exact prepared snapshot remains reusable. The state is a revalidated path-free receipt, not a permanent offline pin.

The same acceptance session found slideshow performance problems. M24 WI-0108 owns slow saved-collection loading, long first-image/startup latency and slow image-to-image transitions; PostgreSQL migration alone is not assumed to fix database-independent repeated file/hash work.

A separate navigation gap observed during PostgreSQL review affects **Collections / Library**, not Smart Collections: opening Photo Details from Collections loses the current result workspace on Back and there is no Previous/Next traversal through that result set. Smart Collection Back-state restoration remains the accepted M19 behavior. Track this as separate navigation follow-up rather than reopening WI-0102.

WI-0076 remains separately recorded as in_progress and is not part of this M24 closeout.

## Next concrete step

For the M24 thread:

1. Verify and merge the WI-0104 generated-once 360px face derivative cache; once green, mark the gallery-image acceptance criterion complete.
2. Finish WI-0104 by splitting cheap Settings archive configuration/shell loading from expensive archive status/storage/filter aggregation and verify independent rendering.
3. Complete WI-0108 slideshow-library/start/playback performance work.
4. Then execute WI-0106 operational PostgreSQL backup/recovery and sustained full-archive catch-up acceptance.

For the M22 thread, WI-0107 remains the focused functional closeout for direct originating-gesture fullscreen launch and durable/revalidated prepared-original receipt state.

## Relevant files

- docs/delivery/work-items/WI-0104-operator-query-ui-performance.md
- docs/delivery/work-items/WI-0106-postgresql-operations-and-archive-catchup.md
- docs/delivery/work-items/WI-0108-slideshow-performance.md
- docs/delivery/work-items/WI-0107-m22-slideshow-acceptance-gaps.md
- docs/operations/postgresql-gallery-query-plan.md
- src/PhotoIdentity.Api/ReviewFacePreviewResolver.cs
- src/PhotoIdentity.Api/FaceReviewImageVariantCache.cs
- src/PhotoIdentity.Api/ReviewEndpoints.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresSuggestionGalleryRepository.cs
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
