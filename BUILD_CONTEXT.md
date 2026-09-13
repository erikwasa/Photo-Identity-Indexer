# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is active.**

WI-0110 is implementing the first low-risk discovery slice: start from one review face, find the nearest eligible faces under one exact embedding revision, and reuse existing audited bulk-review semantics without changing canonical identity thresholds or introducing persistent clusters.

The production execution strategy remains local under ADR-0010, PostgreSQL remains the sole writable production catalogue, M23 remains intentionally deferred, and WI-0081 remains the separate quality investigation that gates any later expansion of automatic identity assignment.

## Next concrete step

Finish PR #319 verification for WI-0110. After automated CI is green, perform representative Windows/mobile-browser verification against the real catalogue: open an eligible face, use `Find similar faces`, confirm deterministic similarity ordering and practical latency, toggle Unknown rediscovery, select a subset with exceptions, and bulk-assign that subset. Record verification evidence before completing WI-0110.

## Relevant files

- docs/delivery/milestones/M25-face-discovery-and-cluster-assisted-review.md
- docs/delivery/work-items/WI-0110-similar-face-explorer.md
- docs/delivery/status/work-items/active/WI-0110.yaml
- src/PhotoIdentity.Core/Review/ISimilarFaceRepository.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresSimilarFaceRepository.cs
- src/PhotoIdentity.Api/SimilarFaceEndpoints.cs
- src/PhotoIdentity.Web/Pages/SimilarFaces.razor
- tests/PhotoIdentity.Persistence.Tests/PostgresSimilarFaceRepositoryTests.cs
- tests/PhotoIdentity.Integration.Tests/SimilarFaceApplicationTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
