# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**Two delivery threads are active: M22 WI-0107 is the next slideshow implementation item, while M24 WI-0097 remains in progress in parallel.**

Consolidated real-phone M22 acceptance passed the implemented slideshow behavior except for two functional gaps now tracked by WI-0107:

1. **Start slideshow** from `/slideshows` must request fullscreen from the initiating tap/click and continue loading/preparation inside fullscreen, without an intermediate application **Enter fullscreen** step when the browser accepts fullscreen.
2. Successful standalone **Prepare originals** state must survive slideshow navigation/page recreation while the exact prepared snapshot remains reusable. The status is a revalidated path-free receipt, not a permanent offline pin.

The same acceptance session found slideshow performance problems. M24 WI-0108 owns slow saved-collection loading, long first-image/startup latency and slow image-to-image transitions. PostgreSQL migration alone is not assumed to fix database-independent repeated file/hash work.

In the M24 thread, the maintainer is on the accepted Podman 5.8.x Windows/WSL baseline: client 5.8.5 and server 5.8.6. PostgreSQL authentication succeeds inside the container and the Windows localhost PostgreSQL protocol preflight passes. PR #236 corrected the verifier exit-code capture bug. SQLite remains authoritative until the live migration bootstrap and application health verification are accepted.

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

For the M24 thread:

1. Rerun `./verify-postgres.ps1` on the accepted Podman 5.8.x runtime.
2. Confirm the live PostgreSQL migration/bootstrap test passes.
3. Verify `/health` reports `catalogueProvider=sqlite`, PostgreSQL `status=ready`, and `schemaVersion=1`.
4. Complete WI-0097 and begin WI-0098 only after that verification is accepted.

Do not migrate or delete the real SQLite catalogue.

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
- docs/delivery/work-items/WI-0097-postgresql-runtime-foundation.md
- verify-postgres.ps1
- docs/delivery/status/work-items.yaml
- docs/delivery/status/milestones.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release

Podman-backed WI-0097 verification:

    ./verify-postgres.ps1
