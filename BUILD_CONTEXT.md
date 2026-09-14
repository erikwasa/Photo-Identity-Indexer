# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is active.**

WI-0110 similar-face discovery, WI-0111 bounded follow-up regeneration, WI-0112 suggested-person grouped review, and WI-0113 provisional-cluster model/algorithm evaluation are complete after implementation, CI, and maintainer verification.

WI-0114 scalable incremental provisional face clustering is implemented in PR #330 and is now `in_review`. CI build #1782 (`34791332956`) is green after adding explicit catalogue-provider integration coverage and production/verification documentation. Maintainer acceptance still requires the live PostgreSQL suite plus a real-catalogue incremental-refresh check before WI-0114 is completed.

The selected production policy remains DBSCAN with `eps=0.30` and `min_samples=3`. WI-0114 uses bounded exact in-process cosine comparisons over an exact-model PostgreSQL snapshot, with a 20,000-face cap and 2,000,000 retained-neighbour-edge safety budget. ANN remains optional and should be introduced only if measured production runtime exceeds the required budget.

Provisional clusters remain derived/non-canonical evidence. Default input is Unreviewed; canonical Unknown participates only through explicit `includeUnknown`; Assigned and Rejected faces are excluded. Cluster runs must never rewrite canonical Person assignments, Unknown decisions, rejection history, suggestions, or review audit history.

PostgreSQL remains the sole writable production catalogue. WI-0081 remains the separate suggestion-quality investigation that gates later automatic identity-assignment expansion.

## Next concrete step

Verify WI-0114 from PR #330:

1. Run `./verify-postgres.ps1` so the live PostgreSQL persistence/integration test bodies execute.
2. On the real PostgreSQL catalogue, complete a provisional cluster run for the current exact embedding model with `includeUnknown=false`; record run/target/cluster/noise counts.
3. Add/analyse a small new photo batch and confirm a replacement cluster run becomes current, the discovery population/groups update, and canonical Person/Unknown/rejection state does not change solely because clustering ran.
4. Optionally verify the separate explicit `includeUnknown=true` scope while canonical Unknown remains unchanged.

Do not start WI-0115 until WI-0114 maintainer acceptance is recorded and WI-0114 is completed.

## Relevant files

- docs/delivery/work-items/WI-0114-scalable-incremental-face-clustering.md
- docs/delivery/status/work-items/active/WI-0114.yaml
- docs/architecture/provisional-face-clustering.md
- src/PhotoIdentity.Core/Clustering/ProvisionalFaceDbscanClusterer.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresProvisionalFaceClusterRepository.cs
- src/PhotoIdentity.Api/ProvisionalFaceClusteringWorker.cs
- src/PhotoIdentity.Api/ProvisionalFaceClusterEndpoints.cs
- tests/PhotoIdentity.Core.Tests/ProvisionalFaceDbscanClustererTests.cs
- tests/PhotoIdentity.Persistence.Tests/PostgresProvisionalFaceClusterRepositoryTests.cs
- tests/PhotoIdentity.Integration.Tests/PostgresRuntimeApplicationTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
    ./verify-postgres.ps1
