# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**WI-0138 is the current focused regression fix: coalesce provisional clustering refreshes during active face review.**

A 2026-09-18 sustained Suggested-groups session showed repeated full provisional-cluster publications after canonical review mutations, including both ~3.5k-face and ~8.1k-face scopes, while ordinary review-list reads remained broadly stable. WI-0138 reopens M25 narrowly to add a durable 30-second review quiet period before automatic replacement clustering.

M27 remains ready for its final real-device acceptance item, WI-0137, after this focused regression work is verified.

## Next concrete step

Complete WI-0138 CI/live PostgreSQL verification, then run a sustained Suggested-groups review session. Confirm review mutations no longer cause one full cluster replacement per decision, stop reviewing for at least 30 seconds, and confirm stale clustering refreshes afterward. Once accepted, return M25 to completed and continue WI-0137.

## Relevant files

- docs/delivery/work-items/WI-0138-coalesced-provisional-cluster-refresh.md
- src/PhotoIdentity.Api/ProvisionalFaceClusteringWorker.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresProvisionalFaceClusterRepository.cs
- tests/PhotoIdentity.Persistence.Tests/PostgresProvisionalFaceClusterRepositoryTests.cs
- docs/architecture/provisional-face-clustering.md
- docs/delivery/work-items/WI-0137-real-device-slideshow-polish-acceptance.md
- docs/delivery/status/work-items/active/WI-0137.yaml
- docs/delivery/work-items/WI-0131-adaptive-slideshow-backdrop.md
- docs/delivery/work-items/WI-0134-adaptive-slideshow-rhythm.md
- docs/delivery/work-items/WI-0136-exception-driven-slideshow-preparation.md
- src/PhotoIdentity.Web/Pages/Slideshows.razor
- src/PhotoIdentity.Web/Pages/Slideshow.razor
- src/PhotoIdentity.Web/Components/SlideshowPresentation.razor

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
