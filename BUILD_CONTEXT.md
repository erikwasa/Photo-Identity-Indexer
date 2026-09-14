# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is active.**

WI-0110 similar-face discovery, WI-0111 bounded follow-up regeneration, WI-0112 suggested-person grouped review, WI-0113 provisional-cluster model/algorithm evaluation, and WI-0114 scalable incremental provisional face clustering are complete after implementation, CI, corrective live-PostgreSQL fixes, and maintainer verification.

WI-0115 cluster-based People-to-identify review was implemented and merged through PR #334, and maintainer Windows/real-catalogue/mobile acceptance completed successfully on 2026-09-14. The accepted slice provides a bounded cluster-card/member workflow, selective assignment through the existing audited bulk-review API, Person creation handoff, durable face-to-face `not same` discovery evidence, and replacement clustering that respects that evidence without changing the accepted DBSCAN policy. The real catalogue produced predominantly pure clusters containing the same person, so intentional false merges were comparatively difficult to find during acceptance.

Post-acceptance usability feedback identified the anchor-face GUID dropdown as disconnected from the displayed faces. The follow-up branch `agent/WI-0115-anchor-selection-polish` makes the first displayed member the default anchor, marks the anchor directly on its face card, and adds a touch-friendly `Make anchor` action to other member cards. This is non-blocking interaction polish; the maintainer plans to recheck it together with a later work item rather than reopen WI-0115 core acceptance.

The selected production policy remains DBSCAN with `eps=0.30` and `min_samples=3`. WI-0114 uses bounded exact in-process cosine comparisons over an exact-model PostgreSQL snapshot, with a 20,000-face cap and 2,000,000 retained-neighbour-edge safety budget. WI-0115 additionally bounds relevant durable not-same evidence per run. ANN remains optional and should be introduced only if measured production runtime exceeds the required budget.

Provisional clusters remain derived/non-canonical evidence. Default input is Unreviewed; canonical Unknown participates only through explicit `includeUnknown`; Assigned and Rejected faces are excluded. Cluster runs and cluster-review feedback must never rewrite canonical Person assignments, Unknown decisions, rejection history, suggestions, or review audit history. Canonical assignment from People to identify reuses the existing reviewed bulk-action path and affects only the operator-selected faces.

PostgreSQL remains the sole writable production catalogue. WI-0081 remains the separate suggestion-quality investigation that gates later automatic identity-assignment expansion.

## Next concrete step

Merge the WI-0115 anchor-selection polish after CI, then archive WI-0115 as completed using the already-passed maintainer acceptance evidence. After that lifecycle update, assess and proceed with WI-0116 cluster-assisted known-person scoring. The anchor-selection polish can be visually rechecked together with that later work rather than blocking WI-0115 closure.

## Relevant files

- docs/delivery/work-items/WI-0115-cluster-discovery-review-workspace.md
- docs/delivery/status/work-items/active/WI-0115.yaml
- docs/architecture/provisional-face-clustering.md
- src/PhotoIdentity.Core/Clustering/IProvisionalFaceClusterReviewRepository.cs
- src/PhotoIdentity.Core/Clustering/ProvisionalFaceDbscanClusterer.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresProvisionalFaceClusterReviewRepository.cs
- src/PhotoIdentity.Api/ProvisionalFaceClusteringWorker.cs
- src/PhotoIdentity.Api/ProvisionalFaceClusterEndpoints.cs
- src/PhotoIdentity.Web/Pages/PeopleToIdentify.razor
- tests/PhotoIdentity.Core.Tests/ProvisionalFaceDbscanClustererTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
    ./verify-postgres.ps1
