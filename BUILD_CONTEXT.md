# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence are resolved by PhotoIdentity.Docs from the current registry plus archived terminal history.

## Current focus

**M22 consolidated phone acceptance is almost complete. WI-0107 is the next implementation item for the M22 thread.**

The maintainer's consolidated real-phone review passed the implemented slideshow behavior except for two functional gaps:

1. **Start slideshow** from `/slideshows` must request fullscreen from the initiating tap and continue loading/preparation inside fullscreen, without an intermediate application **Enter fullscreen** step on a browser that accepts fullscreen.
2. Successful standalone **Prepare originals** state must survive slideshow navigation/page recreation while the exact prepared snapshot remains reusable. The status is a revalidated path-free receipt, not a permanent offline pin.

The same phone review found performance problems, but they are not WI-0107 scope. M24 WI-0108 explicitly owns slow slideshow-library loading, long first-image/startup latency (including an already-prepared one-photo slideshow taking roughly 20 seconds), and slow image-to-image transitions. PostgreSQL migration alone is not assumed to fix database-independent repeated file/hash work.

M22 items WI-0082 through WI-0096 remain in_review until WI-0107 is implemented and the two corrected behaviors are re-verified. The already-passed phone acceptance scenarios do not need to be repeated unless WI-0107 materially touches them.

In the separate M24 thread, **WI-0097 remains in_progress** on `agent/WI-0097-wsl-forwarding-diagnostics`. PR #230 is merged, but Windows localhost still could not reach PostgreSQL even though Podman reported the container listener. The active M24 slice is diagnosing WSL localhost-forwarding/networking before changing the architecture. Do not begin WI-0098 until Windows can reliably reach the PostgreSQL runtime and the migration bootstrap passes.

WI-0108 is proposed behind WI-0101 and is required before final M24 closeout.

WI-0076 remains separately recorded as in_progress and is not part of this M22 slice.

## Next concrete step

For this M22 thread:

1. Merge the documentation/status PR after CI is green.
2. Start WI-0107.
3. Implement direct originating-gesture fullscreen launch from `/slideshows`.
4. Implement path-free successful-preparation receipt persistence plus truthful revalidation across navigation.
5. Run required CI.
6. Re-test only those two remaining M22 scenarios on the real phone.
7. If both pass, record maintainer acceptance and close the M22 work items/milestone.

The separate M24 thread should continue the current WI-0097 WSL-forwarding diagnostics and verification independently.

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
- docs/delivery/status/work-items.yaml
- docs/delivery/status/milestones.yaml
- docs/delivery/work-items/WI-0097-postgresql-runtime-foundation.md
- verify-postgres.ps1

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
