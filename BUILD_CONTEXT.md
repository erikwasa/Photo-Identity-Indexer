# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**WI-0163 Add safe bulk capture-date and Place enrichment remains in progress under M31. WI-0169 Creative Collection scaling is in review under M32 on PR #468. WI-0167 OneDrive-unavailable slideshow recovery is separately in review on PR #467 with CI green. WI-0166 and M33 Archive synchronization performance are completed after maintainer archive-scale acceptance.**

WI-0163 remains on `agent/wi-0163-bulk-metadata-enrichment`; continue its production-catalogue dry-run/apply verification from the canonical work-item shard.

WI-0169 removes the dominant unbounded visual-evidence work from Creative materialization. Candidates that cannot form an accepted same-moment <=20-second visual group are not hashed; remaining durable review-proxy hashes are cached by immutable revision + proxy content hash + visual algorithm version. Proxy/exclusion resolution is set-oriented, hash concurrency is bounded to 4–12 workers, and aggregate phase diagnostics are path-free. Preview/snapshot requests now have a 60-second server deadline and the Creative workspace visibly reports Working state instead of appearing frozen.

WI-0167 is implemented on `agent/wi-0167-onedrive-slideshow-availability`; CI run #2491 passed. It still needs maintained Windows/phone acceptance for OneDrive stopped/online-only, start-and-retry, already-local with OneDrive stopped, and slow active hydration.

M23 Source-copy lifecycle and privacy exclusion is completed. M26, M28, M29 and M33 are completed. M30 video support remains intentionally blocked until explicit maintainer reactivation.

## Next concrete step

For WI-0169, run/confirm PR #468 CI and then reproduce the maintainer's roughly 1,000-anchor / target-150 / Broad case twice on the maintained archive, recording the aggregate Creative materialization timing line and confirming the second run reuses cached visual evidence. Verify the phone/PWA Working/error behavior. Keep WI-0169 in review until those archive-scale checks are accepted. Continue WI-0163 from its canonical shard when returning to metadata-enrichment verification.

## Relevant files

- docs/delivery/work-items/WI-0169-creative-collection-scale.md
- docs/delivery/status/work-items/active/WI-0169.yaml
- src/PhotoIdentity.Api/CreativeCollectionMaterializationService.cs
- src/PhotoIdentity.Api/CreativeCollectionPreviewEndpoints.cs
- src/PhotoIdentity.Api/CreativeVisualFingerprintCache.cs
- src/PhotoIdentity.Api/CollectionReviewProxyFileResolver.cs
- src/PhotoIdentity.Web/Components/SmartCollectionsWorkspace.razor
- tests/PhotoIdentity.Integration.Tests/CreativeCollectionMaterializationOptimizationTests.cs
- docs/delivery/work-items/WI-0167-onedrive-slideshow-availability.md
- docs/delivery/work-items/WI-0163-bulk-metadata-enrichment.md

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-postgres.ps1
