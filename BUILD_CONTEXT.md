# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M22 WI-0107 is implemented in PR #314 and awaiting automated plus real-phone acceptance.**

The corrective slice addresses the two remaining M22 product gaps: `/slideshows` requests fullscreen from the initiating Start slideshow gesture before navigation, and successful standalone preparation now persists as a path-free immutable-revision receipt that is revalidated before **Originals prepared** is restored.

Successful preparation still releases its temporary server lease; the receipt is not an offline pin. Changed collection membership or originals that are no longer local/revision-verified downgrade the prepared state.

M23 remains intentionally deferred. The production execution strategy remains local under ADR-0010.

## Next concrete step

1. Require PR #314 CI/documentation validation to pass.
2. Merge PR #314.
3. On the supported phone/browser, verify Start slideshow enters fullscreen directly with no intermediate **Enter fullscreen** application step when fullscreen is accepted.
4. Verify **Originals prepared** survives slideshow navigation when the exact prepared set remains reusable and disappears when the set becomes stale.
5. If those checks pass, complete WI-0107 and the consolidated M22 `in_review` items and close M22.

## Relevant files

- docs/delivery/work-items/WI-0107-m22-slideshow-acceptance-gaps.md
- docs/delivery/status/work-items/active/WI-0107.yaml
- src/PhotoIdentity.Web/Pages/Slideshows.razor.cs
- src/PhotoIdentity.Web/SlideshowLibraryLaunch.cs
- src/PhotoIdentity.Web/SlideshowPreparationReceipt.cs
- src/PhotoIdentity.Api/SlideshowOriginalPreparationEndpoints.cs
- docs/delivery/milestones/M22-protected-smart-collection-slideshow.md

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
