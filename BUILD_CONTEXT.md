# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**WI-0163 Add safe bulk capture-date and Place enrichment remains in progress under M31. WI-0169 Creative Collection scaling is in review under M32 on PR #468. WI-0167 OneDrive-unavailable slideshow recovery remains in review after PR #467 merged, pending maintained Windows/phone acceptance. WI-0168 PWA slideshow navigation is also in review under M32. WI-0166 and M33 Archive synchronization performance are completed after maintainer archive-scale acceptance.**

WI-0163 remains on `agent/wi-0163-bulk-metadata-enrichment`; continue its production-catalogue dry-run/apply verification from the canonical work-item shard.

WI-0169 removes the dominant unbounded visual-evidence work from Creative materialization. Candidates that cannot form an accepted same-moment <=20-second visual group are not hashed; remaining durable review-proxy hashes are cached by immutable revision + proxy content hash + visual algorithm version. Proxy/exclusion resolution is set-oriented, hash concurrency is bounded to 4–12 workers, and aggregate phase diagnostics are path-free. Preview/snapshot requests now have a 60-second server deadline and the Creative workspace visibly reports Working state instead of appearing frozen.

WI-0167 adds a bounded Windows OneDrive-client absence signal only when hydration is needed. Already-local originals do not depend on OneDrive process availability. A cloud-only best-quality preparation enters an actionable `onedrive-unavailable` recovery state instead of remaining indefinitely at `0 / N`; Retry preserves the immutable slideshow session and can reassert Photo-Identity-owned in-flight hydration after OneDrive starts. Active/unknown but slow downloads retain the existing no-progress recovery. Maintainer acceptance remains outstanding.

M23 Source-copy lifecycle and privacy exclusion is completed. M26, M28, M29 and M33 are completed. M30 video support remains intentionally blocked until explicit maintainer reactivation.

## Next concrete step

WI-0170 review-queue refill implementation is ready for CI and maintained Windows acceptance on branch `agent/wi-0170-review-queue-refill`. Verify bulk Assign and Accept suggestions with 10 selected faces: committed success must appear independently of refill, and repeated/overlapping pages must stop with Reload queue rather than causing a request storm. Use the canonical WI-0170 document for separate commit/refill timing and stale-load checks. Keep it in review until private-catalogue acceptance is recorded.

Continue the independent WI-0169 archive-scale Creative, WI-0167 Windows/phone and WI-0163 metadata-enrichment verification from their canonical work items.

## Relevant files

- docs/delivery/work-items/WI-0170-review-queue-refill.md
- docs/delivery/status/work-items/active/WI-0170.yaml
- src/PhotoIdentity.Web/Components/ReviewWorkspace.razor
- tests/PhotoIdentity.Integration.Tests/ReviewWorkspacePagingTests.cs
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
