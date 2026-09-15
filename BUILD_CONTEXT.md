# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is in progress on WI-0117. WI-0110 through WI-0116 are complete.**

WI-0081 accepted the current max-exemplar matcher and current High score+margin policy after private evaluation: duplicate-resistant top-1 was 96.491%, conservative High precision 99.844%, known High coverage 49.709% and reviewed-Unknown High emission 0.121%. Centroid and cap-8 reference reduction regressed materially.

WI-0116 accepted Strong cluster-assisted advisory evidence at 41/41 correct evaluable proposals with 0 false-person proposals. Cluster evidence remains derived/non-canonical and production `not same` evidence fails closed.

WI-0117 private evaluation is complete. On 5,747 untouched holdout targets, the selected `cluster-plus-2x-0.50-margin-0.05` candidate added 50 assignments, all 50 correct known, with 0 additional wrong-known and 0 additional reviewed-Unknown assignments. Known coverage increased from 48.195% to 49.406%. Multi-reference-only expansion was unsafe and is explicitly rejected.

The accepted production candidate is `m25-multi-evidence-auto-v1`: retain current High auto-assignment unchanged, and optionally add only Medium-or-better rank-1 candidates with margin >=0.05, at least 2 independent exact-content references to the same Person at cosine >=0.50, and target-specific Strong WI-0116 cluster corroboration from a fresh completed `m25-dbscan-v1` include-Unknown run. Same-content target evidence is excluded and `not same`/material competing-person evidence fails closed.

Implementation is active on `agent/WI-0117-multi-evidence-production`. The new exact-model multi-evidence policy is separately versioned, default-disabled, and subordinate to the existing ordinary automatic-assignment master toggle. Candidate evidence is read before any canonical accepts so same-run cascading remains prohibited. Canonical decisions use the normal suggestion acceptance/history boundary with a distinct actor and detailed model/policy/cluster/reference provenance.

PostgreSQL remains the sole writable production catalogue.

## Next concrete step

Finish CI and production verification for `m25-multi-evidence-auto-v1`. Required checks are: policy default-off/versioning, fresh-cluster and independent-reference gates, `not same` fail-closed behavior, exact provenance, fixed-snapshot/no-cascade, operator opt-in, and manual correction/undo. Do not mark WI-0117 complete until human Windows verification of opt-in plus correction/undo passes.

## Relevant files

- docs/delivery/work-items/WI-0117-multi-evidence-auto-assignment-evaluation.md
- docs/delivery/status/work-items/active/WI-0117.yaml
- tools/cluster-evaluation/evaluate_auto_assignment.py
- src/PhotoIdentity.Core/Review/IIdentityMultiEvidenceAutoAssignmentPolicyRepository.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresIdentityMultiEvidenceAutoAssignmentPolicyRepository.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresIdentityAutoAssignmentService.cs
- src/PhotoIdentity.Web/Components/MultiEvidenceAutoAssignmentSettings.razor
- docs/decisions/ADR-0006-canonical-auto-assignment.md

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
    ./verify-postgres.ps1
