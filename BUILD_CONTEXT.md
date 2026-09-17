# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M27 Slideshow presentation experience is continuing with WI-0136: make slideshow startup and original preparation exception-driven.**

The M22 preparation lifecycle is already safe and automatic inside the slideshow when the persisted Prepare originals preference is enabled. WI-0136 keeps that storage/preflight/hydration/verification contract intact while removing routine preparation operations from each visual gallery card, simplifying happy-path preparation progress and reserving explicit recovery for no-progress or failed preparation states.

Standalone pre-preparation remains available under collapsed parent preparation tools. No-progress recovery now offers retry, continue-with-available playback or cancellation; capacity and immutable-verification failures retain explicit degraded-playback recovery. Real-phone verification is intentionally happening in the separate verification thread rather than blocking implementation work here.

## Next concrete step

Validate the WI-0136 branch through the normal build/integration/docs gates. Then verify one Prepare originals Off slideshow and one Prepare originals On slideshow on the supported phone path, including a representative exception recovery, before closing the item. Broader device/polish acceptance remains with WI-0137.

## Relevant files

- docs/delivery/work-items/WI-0136-exception-driven-slideshow-preparation.md
- docs/delivery/status/work-items/active/WI-0136.yaml
- src/PhotoIdentity.Web/Pages/Slideshows.razor
- src/PhotoIdentity.Web/Pages/Slideshow.razor
- src/PhotoIdentity.Web/SlideshowPreparationExperience.cs
- tests/PhotoIdentity.Integration.Tests/SlideshowPreparationExperienceTests.cs
- tests/PhotoIdentity.Integration.Tests/SlideshowOriginalPreparationServiceTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
