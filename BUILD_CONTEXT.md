# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M22 WI-0107 remains in real-phone acceptance. PR #314 merged, and follow-up PR #315 fixes a browser-format preparation blocker found during that verification.**

PR #314 addressed the two previously known M22 product gaps: `/slideshows` requests fullscreen from the initiating Start slideshow gesture before navigation, and successful standalone preparation persists as a path-free immutable-revision receipt that is revalidated before **Originals prepared** is restored.

Real-phone verification then exposed an additional preparation gap: a local, immutable-revision-verified original whose media type is not directly browser-renderable (for example HEIC) caused the entire **Prepare originals** session to fail even though the normal viewer already has a durable proxy fallback for that case.

PR #315 keeps the existing all-original hydration/verification contract, treats browser decode support as a playback concern rather than a preparation failure, and routes browser-unsupported prepared media through the established viewer-preview fallback. Browser-supported prepared media continues to use its verified original.

Successful standalone preparation still releases its temporary server lease; the receipt is not an offline pin. Changed collection membership or originals that are no longer local/revision-verified downgrade the prepared state.

M23 remains intentionally deferred. The production execution strategy remains local under ADR-0010.

## Next concrete step

1. Require PR #315 CI/documentation validation to pass and merge it.
2. On the supported phone/browser, rerun the exact **Prepare originals** operation that previously reported `One or more verified originals cannot be rendered directly by this browser` and confirm preparation completes.
3. Start the slideshow and confirm browser-unsupported prepared photos display through their durable proxy while browser-supported photos still use verified originals.
4. Verify Start slideshow enters fullscreen directly with no intermediate **Enter fullscreen** application step when fullscreen is accepted.
5. Verify **Originals prepared** survives slideshow navigation when the exact prepared set remains reusable and disappears when the set becomes stale.
6. If those checks pass, complete WI-0107 and the consolidated M22 `in_review` items and close M22.

## Relevant files

- docs/delivery/work-items/WI-0107-m22-slideshow-acceptance-gaps.md
- docs/delivery/status/work-items/active/WI-0107.yaml
- src/PhotoIdentity.Web/Pages/Slideshows.razor.cs
- src/PhotoIdentity.Web/SlideshowLibraryLaunch.cs
- src/PhotoIdentity.Web/SlideshowPreparationReceipt.cs
- src/PhotoIdentity.Api/SlideshowOriginalPreparationService.cs
- src/PhotoIdentity.Api/SlideshowOriginalPreparationEndpoints.cs
- tests/PhotoIdentity.Integration.Tests/SlideshowBrowserFormatPreparationApplicationTests.cs
- docs/delivery/milestones/M22-protected-smart-collection-slideshow.md

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
