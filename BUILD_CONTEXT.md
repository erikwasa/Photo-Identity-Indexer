# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**M22 WI-0107 is the next slideshow implementation item. M24 WI-0098 continues in parallel.**

Consolidated real-phone M22 acceptance passed the implemented slideshow behavior except for two functional gaps tracked by WI-0107:

1. **Start slideshow** from `/slideshows` must request fullscreen from the initiating tap/click and continue loading/preparation inside fullscreen without an intermediate application **Enter fullscreen** step when the browser accepts fullscreen.
2. Successful standalone **Prepare originals** state must survive slideshow navigation/page recreation while the exact prepared snapshot remains reusable. The state is a revalidated path-free receipt, not a permanent offline pin.

The same acceptance session found slideshow performance problems. M24 WI-0108 owns slow saved-collection loading, long first-image/startup latency and slow image-to-image transitions; PostgreSQL migration alone is not assumed to fix database-independent repeated file/hash work.

In the separate M24 thread, WI-0097 is maintainer-verified and completed. **WI-0098 — Add database-neutral persistence boundary and foundational PostgreSQL schema** is in progress on `agent/WI-0098-local-batch-catalogue-boundary`. PR #242 merged the processing-run lifecycle boundary. The active slice adds neutral catalogue initialization/source-scan contracts and removes the remaining direct SQLite persistence dependency from `LocalBatchCoordinator`; SQLite remains authoritative.

WI-0076 remains separately recorded as in_progress and is not part of this M22 slice.

## Next concrete step

For the M22 thread:

1. Merge this documentation/status PR after CI is green.
2. Start WI-0107.
3. Implement direct originating-gesture fullscreen launch from `/slideshows`.
4. Implement path-free successful-preparation receipt persistence plus truthful revalidation across navigation.
5. Run required CI.
6. Re-test only those two remaining M22 scenarios on the real phone.
7. If both pass, record maintainer acceptance and close the M22 work items/milestone.

For the M24 thread, review/merge the WI-0098 local-batch catalogue boundary after CI is green. Keep the slice contract-only: preserve current SQLite source registration, scan, revision-selection and processing behavior and do not introduce PostgreSQL authoritative reads/writes or dual writes yet. After merge, reassess WI-0098 acceptance against the remaining foundational paths before starting WI-0099.

## Relevant files

- docs/delivery/work-items/WI-0107-m22-slideshow-acceptance-gaps.md
- docs/delivery/milestones/M22-protected-smart-collection-slideshow.md
- docs/product/slideshow.md
- src/PhotoIdentity.Web/Pages/Slideshows.razor
- src/PhotoIdentity.Web/Pages/Slideshows.razor.cs
- src/PhotoIdentity.Web/Pages/Slideshow.razor.cs
- src/PhotoIdentity.Web/wwwroot/js/slideshow.js
- docs/delivery/work-items/WI-0108-slideshow-performance.md
- docs/delivery/milestones/M24-postgresql-catalogue-and-scale.md
- docs/delivery/work-items/WI-0098-persistence-boundary-foundational-schema.md
- docs/delivery/status/work-items.yaml
- docs/delivery/status/milestones.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release

Live PostgreSQL migration verification for the M24 thread:

    ./verify-postgres.ps1
