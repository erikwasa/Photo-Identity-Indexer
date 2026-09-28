# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**WI-0163 Add safe bulk capture-date and Place enrichment remains in progress under M31. WI-0167 OneDrive-unavailable slideshow recovery is in review under M32 on PR #467. WI-0166 and M33 Archive synchronization performance are completed after maintainer archive-scale acceptance.**

WI-0163 remains on `agent/wi-0163-bulk-metadata-enrichment`; continue its production-catalogue dry-run/apply verification from the canonical work-item shard.

WI-0167 adds a bounded Windows OneDrive-client absence signal only when hydration is needed. Already-local originals do not depend on OneDrive process availability. A cloud-only best-quality preparation now enters an actionable `onedrive-unavailable` recovery state instead of remaining indefinitely at `0 / N`; Retry preserves the immutable slideshow session and can reassert Photo-Identity-owned in-flight hydration after OneDrive starts. Active/unknown but slow downloads retain the existing no-progress recovery.

M23 Source-copy lifecycle and privacy exclusion is completed. M26, M28, M29 and M33 are completed. M30 video support remains intentionally blocked until explicit maintainer reactivation.

## Next concrete step

Run PR #467 CI, then verify WI-0167 on the maintained Windows archive PC/phone: online-only with OneDrive stopped, start OneDrive and Retry, already-local with OneDrive stopped, and a slow active hydration. Keep WI-0167 in review until that acceptance is recorded. Continue WI-0163 from its canonical shard when returning to metadata-enrichment verification.

## Relevant files

- docs/delivery/work-items/WI-0167-onedrive-slideshow-availability.md
- docs/delivery/status/work-items/active/WI-0167.yaml
- src/PhotoIdentity.Source.OneDriveSync/OneDriveFilesOnDemandPlatform.cs
- src/PhotoIdentity.Api/CollectionOriginalAccessService.cs
- src/PhotoIdentity.Api/SlideshowOriginalPreparationService.cs
- tests/PhotoIdentity.Integration.Tests/SlideshowOneDriveAvailabilityTests.cs
- docs/delivery/work-items/WI-0163-bulk-metadata-enrichment.md
- docs/delivery/status/work-items/active/WI-0163.yaml

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
