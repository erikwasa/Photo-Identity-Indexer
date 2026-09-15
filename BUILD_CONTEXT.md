# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is active.**

WI-0110 through WI-0115 are complete after implementation, CI and maintainer verification. WI-0115 provides bounded cluster-card/member review, selective canonical assignment, durable face-to-face `not same` evidence and replacement clustering. Its post-acceptance anchor polish from PR #336 defaults to the first displayed face and uses direct `Make anchor` actions; the maintainer plans to visually recheck that interaction with a later work item.

WI-0116 `Add cluster-assisted known-person advisory evidence` is in implementation on `agent/WI-0116-cluster-known-person-advisory`. The initial advisory policy `m25-cluster-known-person-v1` combines only current exact-model rank-1 suggestion evidence with one current provisional cluster. A qualifying vote must meet the existing ordinary Medium threshold. Strong advisory support requires at least 3 independent votes for one Person, at least 60% cluster support and at least 60% Core membership. More than 1 qualifying competing vote, more than 20% competing support, or current internal `not same` evidence fails closed to Ambiguous. One strong member never causes cluster-wide identity inheritance.

The advisory projection is read-only and regenerated on demand from current versioned cluster/suggestion evidence. It does not lower the ordinary High threshold, overwrite per-face rank/score/margin evidence, preselect a Person, select faces, or create canonical assignments. Rejected exact face-person pairs and canonically reviewed faces are excluded. The API/UI exposes exact-model, cluster-policy, advisory-policy and ordinary suggestion-policy provenance plus support/competition explanations.

Private WI-0116 evaluation reuses the pseudonymized WI-0113 exact-model export. `tools/cluster-evaluation/evaluate_advisory.py` fixes clustering to production DBSCAN (`eps=0.30`, `min_samples=3`), simulates ordinary known-person ranks without self-matching, and reports false-person proposals, conservative precision/recall, mixed-cluster Strong outcomes and estimated review-task compression. Samples and reports remain private/uncommitted.

PostgreSQL remains the sole writable production catalogue. Provisional clusters and cluster-assisted identity evidence remain derived/non-canonical. WI-0081 remains the separate suggestion-quality investigation that gates later automatic identity-assignment expansion.

## Next concrete step

Finish CI and live PostgreSQL validation for WI-0116, then run the private reviewed-sample advisory evaluation and perform maintainer review of representative Strong/Ambiguous/Insufficient cluster panels. During that review also visually recheck WI-0115's card-based anchor selection. Do not start WI-0117 or enable automatic assignment from cluster evidence during WI-0116.

## Relevant files

- docs/delivery/work-items/WI-0116-cluster-assisted-known-person-evidence.md
- docs/delivery/status/work-items/active/WI-0116.yaml
- docs/architecture/provisional-face-clustering.md
- src/PhotoIdentity.Core/Clustering/ProvisionalFaceClusterKnownPersonAdvisory.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresProvisionalFaceClusterKnownPersonAdvisoryRepository.cs
- src/PhotoIdentity.Api/ProvisionalFaceClusterEndpoints.cs
- src/PhotoIdentity.Web/Pages/PeopleToIdentify.razor
- tools/cluster-evaluation/evaluate_advisory.py
- tests/PhotoIdentity.Core.Tests/ProvisionalFaceClusterKnownPersonAdvisoryTests.cs
- tests/PhotoIdentity.Persistence.Tests/PostgresProvisionalFaceClusterKnownPersonAdvisoryRepositoryTests.cs

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
    ./verify-postgres.ps1
