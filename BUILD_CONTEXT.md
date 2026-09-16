# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M27 Slideshow presentation experience is continuing with a WI-0130 corrective slice for motion visibility and diagnostics.**

The 2026-09-16 combined maintainer review accepted WI-0129 dual-layer transitions and WI-0135 visual tap-to-play library behavior. WI-0130 was not accepted because no foreground motion was detectable on either Android or PC.

The merged WI-0130 path is functionally wired from identity-free face geometry through the motion policy into the dual-layer compositor, but its original 1.5% `ease-in-out` zoom over the full slide duration proved too subtle for real-device verification. The corrective slice raises the bounded scale-only zoom to 3% with linear progression while keeping the existing conservative safety policy: more than two faces, edge-near/tight/invalid faces, unavailable geometry, or geometry request failures remain static.

The visible presentation layers now also expose identity-free `data-photoidentity-motion` diagnostics. During real-device inspection, `active`/`paused` confirms that the motion policy is actually applied, while `static-policy` and `static-geometry-unavailable` distinguish intentional framing fallback from a geometry delivery problem.

## Next concrete step

Validate the corrective WI-0130 slice through the normal build/integration/docs gates, then repeat Android/PC slideshow review. Confirm that suitable photos now show restrained but detectable motion, face-risk cases stay static, pause/resume preserves position, and reduced-motion disables the foreground animation. Do not add viewer-facing motion controls.

## Relevant files

- docs/delivery/work-items/WI-0129-dual-layer-slideshow-renderer.md
- docs/delivery/work-items/WI-0135-visual-tap-to-play-slideshow-library.md
- docs/delivery/work-items/WI-0130-subtle-motion-face-aware-framing.md
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
