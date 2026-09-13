# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is active.**

WI-0110 similar-face discovery is complete. WI-0111 bounded follow-up regeneration and WI-0112 suggested-person grouped review are implemented/merged and remain `in_review` because the maintainer deferred their human acceptance pass.

WI-0113 provisional-cluster model/algorithm evaluation is now the active implementation item on `agent/WI-0113-cluster-evaluation`. It defines provisional clusters as exact-model, policy-versioned derived evidence, adds a bounded PostgreSQL reviewed-sample exporter with private pseudonymized output, and adds local DBSCAN/HDBSCAN/mutual-neighbour evaluation tooling. No production clustering threshold or ANN choice may be recorded until the maintainer runs the private reviewed sample.

PostgreSQL remains the sole writable production catalogue. WI-0081 remains the separate suggestion-quality investigation that gates later automatic identity-assignment expansion.

## Next concrete step

Get WI-0113 CI green, then mark it `in_review` rather than completed. After merge, run one combined maintainer verification session for WI-0111, WI-0112, and WI-0113.

The WI-0113 private pass must record the chosen conservative algorithm/policy from measured false-merge/split/noise/coverage results and decide whether exact PostgreSQL/vector-neighbour retrieval is sufficient for WI-0114 or an ANN index is justified. Do not start WI-0114 before that decision is recorded.

## Relevant files

- docs/delivery/work-items/WI-0111-event-driven-match-regeneration.md
- docs/delivery/work-items/WI-0112-suggested-person-review-workspace.md
- docs/delivery/work-items/WI-0113-provisional-cluster-model-and-algorithm-evaluation.md
- docs/architecture/provisional-face-clustering.md
- src/PhotoIdentity.Core/Clustering/ProvisionalFaceClusterContracts.cs
- src/PhotoIdentity.Core/Clustering/IProvisionalClusterEvaluationRepository.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresProvisionalClusterEvaluationRepository.cs
- tools/PhotoIdentity.ClusterEvaluation/Program.cs
- tools/cluster-evaluation/evaluate.py
- tools/cluster-evaluation/README.md

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
