# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M27 Slideshow presentation experience is continuing with WI-0130: subtle automatic motion and face-aware slideshow framing.**

WI-0129 and WI-0135 are merged but intentionally remain `in_progress` until the maintainer performs the requested combined visual/device review. WI-0130 may build on the merged WI-0129 compositor without treating that deferred acceptance as complete.

WI-0130 reuses `IFaceReviewDerivativeRepository.GetFacesAsync` to expose only normalized, identity-free face rectangles for the incoming slideshow revision. The two-layer compositor loads that geometry alongside image decode and applies a conservative deterministic policy: a 1.5% zoom around a safe face-region pivot for at most two suitable faces, deterministic near-center motion for reliable no-face geometry, and static fallback for groups, edge-near/tight/invalid faces, missing geometry, or request failures. CSS `animation-play-state` preserves motion position across pause/resume, and `prefers-reduced-motion` disables the effect.

## Next concrete step

Validate the WI-0130 implementation through the normal build/integration/docs gates. In the combined maintainer review, compare #346 crossfades, #347 visual slideshow selection, and WI-0130 motion across portraits, landscapes, close-ups, group photos, pause/resume, manual navigation and reduced-motion behavior. Tune the fixed constants rather than adding viewer-facing motion controls.

## Relevant files

- docs/delivery/work-items/WI-0130-subtle-motion-face-aware-framing.md
- docs/delivery/status/work-items/active/WI-0130.yaml
- src/PhotoIdentity.Api/CollectionViewerPreviewEndpoints.cs
- src/PhotoIdentity.Web/SlideshowPresentationContracts.cs
- src/PhotoIdentity.Web/SlideshowMotionPolicy.cs
- src/PhotoIdentity.Web/Components/SlideshowPresentation.razor
- src/PhotoIdentity.Web/Components/SlideshowPresentation.razor.css
- src/PhotoIdentity.Web/Pages/Slideshow.razor
- tests/PhotoIdentity.Integration.Tests/SlideshowMotionPolicyTests.cs
- tests/PhotoIdentity.Integration.Tests/SlideshowFaceGeometryApplicationTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
