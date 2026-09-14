# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is ready for the next work item.**

WI-0110 similar-face discovery, WI-0111 bounded follow-up regeneration, WI-0112 suggested-person grouped review, WI-0113 provisional-cluster model/algorithm evaluation, and WI-0114 scalable incremental provisional face clustering are complete after implementation, CI, corrective live-PostgreSQL fixes, and maintainer verification.

WI-0114 final acceptance passed on 2026-09-14. `verify-postgres.ps1` succeeded after corrective PRs #331 and #332. On the real catalogue, new analysis evidence replaced cluster run `09a848a0-4e62-42ba-8330-44fa2c7ec0be` with `7b6c56d8-1fae-43c5-a776-8d16149b6655`; target count increased from 5,183 to 5,191, cluster count remained 60, noise count increased from 4,792 to 4,800, and canonical Assigned/Unknown/Rejected totals remained exactly 10,185/4,116/643.

The selected production policy remains DBSCAN with `eps=0.30` and `min_samples=3`. WI-0114 uses bounded exact in-process cosine comparisons over an exact-model PostgreSQL snapshot, with a 20,000-face cap and 2,000,000 retained-neighbour-edge safety budget. ANN remains optional and should be introduced only if measured production runtime exceeds the required budget.

Provisional clusters remain derived/non-canonical evidence. Default input is Unreviewed; canonical Unknown participates only through explicit `includeUnknown`; Assigned and Rejected faces are excluded. Cluster runs must never rewrite canonical Person assignments, Unknown decisions, rejection history, suggestions, or review audit history.

PostgreSQL remains the sole writable production catalogue. WI-0081 remains the separate suggestion-quality investigation that gates later automatic identity-assignment expansion.

## Next concrete step

Prepare and implement WI-0115 cluster-based People-to-identify review workspace as a separate work item after the WI-0114 closeout PR is merged.

Preserve the WI-0114 boundaries when designing WI-0115:

1. Treat provisional cluster membership as disposable derived evidence, never as identity truth.
2. Keep exact model/policy provenance visible enough for review behavior to remain auditable.
3. Do not automatically assign people or rewrite canonical Unknown/rejection state from cluster membership.
4. Keep cluster review bounded and usable for the real ~5k unreviewed-face population.
5. Do not expand into WI-0116 cluster-assisted known-person scoring in the same PR.

## Relevant files

- docs/delivery/work-items/WI-0115-cluster-discovery-review-workspace.md
- docs/delivery/status/work-items/active/WI-0115.yaml
- docs/architecture/provisional-face-clustering.md
- src/PhotoIdentity.Core/Clustering/ProvisionalFaceDbscanClusterer.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresProvisionalFaceClusterRepository.cs
- src/PhotoIdentity.Api/ProvisionalFaceClusteringWorker.cs
- src/PhotoIdentity.Api/ProvisionalFaceClusterEndpoints.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
    ./verify-postgres.ps1
