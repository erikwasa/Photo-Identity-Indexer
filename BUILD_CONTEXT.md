# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is active.**

WI-0110 similar-face discovery, WI-0111 bounded follow-up regeneration, WI-0112 suggested-person grouped review, WI-0113 provisional-cluster model/algorithm evaluation, WI-0114 scalable incremental provisional face clustering, and WI-0115 cluster-based People-to-identify review are complete after implementation, CI and maintainer verification.

WI-0115 was implemented through PR #334 and accepted by the maintainer on 2026-09-14 using the local PostgreSQL-backed application, real catalogue and mobile-width/touch review. The accepted slice provides bounded cluster-card/member review, selective assignment through the audited bulk-review API, Person creation handoff, durable face-to-face `not same` discovery evidence, and replacement clustering that respects that evidence without changing the accepted DBSCAN policy. The real catalogue produced predominantly pure single-person clusters, so intentional false merges were comparatively difficult to find during acceptance.

Post-acceptance usability feedback identified the anchor-face GUID dropdown as disconnected from the displayed faces. PR #336, merged on 2026-09-15, replaced it with first-visible-face default anchoring, a direct Anchor badge and touch-friendly `Make anchor` actions on the member cards. This polish does not reopen WI-0115 acceptance; the maintainer plans to visually recheck the interaction together with a later work item.

The selected production policy remains DBSCAN with `eps=0.30` and `min_samples=3`. WI-0114 uses bounded exact in-process cosine comparisons over an exact-model PostgreSQL snapshot, with a 20,000-face cap and 2,000,000 retained-neighbour-edge safety budget. WI-0115 additionally bounds relevant durable not-same evidence per run. ANN remains optional and should be introduced only if measured production runtime exceeds the required budget.

Provisional clusters remain derived/non-canonical evidence. Default input is Unreviewed; canonical Unknown participates only through explicit `includeUnknown`; Assigned and Rejected faces are excluded. Cluster runs and cluster-review feedback must never rewrite canonical Person assignments, Unknown decisions, rejection history, suggestions, or review audit history. Canonical assignment from People to identify reuses the existing reviewed bulk-action path and affects only the operator-selected faces.

PostgreSQL remains the sole writable production catalogue. WI-0081 remains the separate suggestion-quality investigation that gates later automatic identity-assignment expansion.

## Next concrete step

Assess WI-0116 `Add cluster-assisted known-person advisory evidence` before changing its lifecycle state or starting implementation. Its declared dependencies, WI-0114 and WI-0043, are complete, so the work is technically unblocked. The review should confirm the exact advisory scoring/evaluation slice, fail-closed mixed-cluster behavior and how cluster-level evidence should appear beside existing per-face rank/score/margin evidence. Do not expand into WI-0117 automatic multi-evidence assignment during WI-0116.

## Relevant files

- docs/delivery/work-items/WI-0116-cluster-assisted-known-person-evidence.md
- docs/delivery/status/work-items/active/WI-0116.yaml
- docs/delivery/status/work-items/archive/WI-0115.yaml
- docs/architecture/provisional-face-clustering.md
- src/PhotoIdentity.Core/Clustering/IProvisionalFaceClusterReviewRepository.cs
- src/PhotoIdentity.Core/Clustering/ProvisionalFaceDbscanClusterer.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresProvisionalFaceClusterReviewRepository.cs
- src/PhotoIdentity.Api/ProvisionalFaceClusterEndpoints.cs
- src/PhotoIdentity.Web/Pages/PeopleToIdentify.razor

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
    ./verify-postgres.ps1
