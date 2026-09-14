# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is active.**

WI-0110 similar-face discovery, WI-0111 bounded follow-up regeneration, WI-0112 suggested-person grouped review, WI-0113 provisional-cluster model/algorithm evaluation, and WI-0114 scalable incremental provisional face clustering are complete after implementation, CI, corrective live-PostgreSQL fixes, and maintainer verification.

WI-0115 cluster-based People-to-identify review is now in implementation on `agent/WI-0115-cluster-review-workspace`. The current slice adds a bounded cluster-card/member workflow, selective assignment through the existing audited bulk-review API, Person creation handoff, durable face-to-face `not same` discovery evidence, and replacement clustering that respects that evidence without changing the accepted DBSCAN policy.

The selected production policy remains DBSCAN with `eps=0.30` and `min_samples=3`. WI-0114 uses bounded exact in-process cosine comparisons over an exact-model PostgreSQL snapshot, with a 20,000-face cap and 2,000,000 retained-neighbour-edge safety budget. WI-0115 additionally bounds relevant durable not-same evidence per run. ANN remains optional and should be introduced only if measured production runtime exceeds the required budget.

Provisional clusters remain derived/non-canonical evidence. Default input is Unreviewed; canonical Unknown participates only through explicit `includeUnknown`; Assigned and Rejected faces are excluded. Cluster runs and cluster-review feedback must never rewrite canonical Person assignments, Unknown decisions, rejection history, suggestions, or review audit history. Canonical assignment from People to identify reuses the existing reviewed bulk-action path and affects only the operator-selected faces.

PostgreSQL remains the sole writable production catalogue. WI-0081 remains the separate suggestion-quality investigation that gates later automatic identity-assignment expansion.

## Next concrete step

Finish automated/CI validation for WI-0115, then perform maintainer Windows and real/mobile-browser acceptance on the real catalogue before closing the work item.

Representative acceptance should confirm:

1. `People to identify` shows useful provisional groups with representative faces and explicit derived status.
2. Opening a group loads a bounded member set with photo-context links and remains usable at mobile width/touch.
3. Selecting only obvious members and assigning them to an existing/new Person uses canonical audited review actions while unselected members remain unreviewed.
4. Recording `not same as anchor` for an intentional exception writes durable discovery evidence, queues a replacement run, and the replacement grouping respects that conflict.
5. Canonical Person/Unknown/rejection history is not rewritten merely because grouping or negative discovery feedback changed.

Do not start WI-0116 cluster-assisted known-person scoring until WI-0115 implementation, CI, and maintainer acceptance are complete.

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
