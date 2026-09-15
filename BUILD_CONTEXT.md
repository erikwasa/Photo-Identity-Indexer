# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is in progress on WI-0117. WI-0110 through WI-0116 are complete.**

WI-0081 accepted the current max-exemplar matcher and current High score+margin policy after private evaluation: duplicate-resistant top-1 was 96.491%, conservative High precision 99.844%, known High coverage 49.709% and reviewed-Unknown High emission 0.121%. Quality problems were concentrated in weak/small faces and sparse identities; centroid and cap-8 reference reduction regressed materially. The corrected exact-content audit found no cross-label near-duplicate contamination signal.

WI-0116 accepted Strong cluster-assisted advisory evidence at 41/41 correct evaluable proposals with 0 false-person proposals. Cluster evidence remains derived/non-canonical and production `not same` evidence fails closed.

WI-0117 `Evaluate multi-evidence automatic identity assignment` is now active on `agent/WI-0117-multi-evidence-evaluation`. The first slice is evaluation-only: `tools/cluster-evaluation/evaluate_auto_assignment.py` reuses the WI-0081 private exact-model export, evaluates current High against multi-reference, Strong-cluster and combined candidate expansions, and does not change production thresholds or canonical assignment behavior.

Policy selection is split deterministically by exact-content group (60% selection / 40% holdout). Ranking removes same-content references, and DBSCAN/cluster votes are constructed independently inside each partition. Candidate expansion is limited to Medium-or-better targets and is shortlisted only when selection-split false-assignment/Unknown guardrails are preserved.

PostgreSQL remains the sole writable production catalogue. ADR-0006 fixed-snapshot/no-same-run-cascade, exact-policy provenance and manual supersession rules remain mandatory if any broader policy is later accepted.

## Next concrete step

Merge the WI-0117 evaluation tooling after CI, then run `evaluate_auto_assignment.py` privately against the existing `private/cluster-evaluation/sample.json`. Record only aggregate baseline/selection/holdout/quality results. Do not implement broader production automatic assignment unless the private holdout demonstrates an acceptable precision, Unknown-rejection and review-effort trade-off.

If no candidate preserves those guardrails, close WI-0117 with current High-only automatic assignment unchanged.

## Relevant files

- docs/delivery/work-items/WI-0117-multi-evidence-auto-assignment-evaluation.md
- docs/delivery/status/work-items/active/WI-0117.yaml
- tools/cluster-evaluation/evaluate_auto_assignment.py
- tools/cluster-evaluation/README.md
- src/PhotoIdentity.Persistence.Postgres/PostgresIdentityAutoAssignmentService.cs
- src/PhotoIdentity.Core/Clustering/ProvisionalFaceClusterKnownPersonAdvisory.cs
- docs/decisions/ADR-0006-canonical-auto-assignment.md

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
    ./verify-postgres.ps1
