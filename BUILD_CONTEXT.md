# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is ready, with WI-0110 through WI-0116 complete and WI-0117 now unblocked.**

WI-0116 `Add cluster-assisted known-person advisory evidence` was implemented in PR #338 and accepted by the maintainer on 2026-09-15. Private maintainer evaluation produced 41 Strong, 25 Ambiguous and 17 Insufficient clusters. All 41 evaluable Strong proposals were correct, with 0 false-person proposals and 100.000% proposal precision; pure reviewed opportunity recall was 63.077% (41/65), none of the 2 mixed reviewed clusters reached Strong, and estimated review compression was 6.82x.

WI-0081 `Investigate degraded identity suggestion accuracy` is complete. The 2026-09-15 private exact-model evaluation covered 14,469 reviewed targets, 10,353 production references and 160 identities. Duplicate-resistant max-exemplar ranking measured 96.491% top-1 / 98.468% top-3 / 98.924% top-5, with 99.844% conservative High precision, 49.709% known High coverage and 0.121% reviewed-Unknown High emission. Degradation was concentrated in weak/small faces and sparse identities rather than monotonic catalogue-size decay. Centroid and cap-8 reference reductions regressed materially.

The corrected exact-content audit found 416 reference content groups spanning multiple photo revisions and 0 cross-label near-duplicate candidates at cosine >=0.95 (maximum cross-label similarity 0.4317). The maintainer selected keeping the current max-exemplar ranking and current High score+margin policy unchanged. Future quality-aware handling of weak/small faces or sparse identities must preserve the accepted false-positive guardrails.

WI-0117 `Evaluate multi-evidence automatic identity assignment` has both declared dependencies satisfied: WI-0081 and WI-0116 are complete. It remains a proposed work item, but dependency readiness now makes M25 ready. WI-0117 is explicitly an evaluation gate: broader automatic assignment is optional and may close with current automation unchanged if private evidence does not demonstrate an acceptable precision/unknown-rejection/review-effort trade-off.

PostgreSQL remains the sole writable production catalogue. Provisional clusters and cluster-assisted identity evidence remain derived/non-canonical.

## Next concrete step

Reassess and start WI-0117 if continuing M25. Define private candidate multi-evidence rules that combine independent exemplar and cluster support, compare them against the accepted current High policy, and fail closed unless false assignment/Unknown rejection guardrails are preserved. Do not broaden production automatic-assignment semantics before the private evaluation supports it.

If M25 automation is intentionally deferred, M26 Creative Collections has WI-0118 `Prototype timestamp-first photo moment clustering` ready as an independent next direction.

## Relevant files

- docs/delivery/work-items/WI-0117-multi-evidence-auto-assignment-evaluation.md
- docs/delivery/status/work-items/active/WI-0117.yaml
- docs/delivery/work-items/WI-0081-suggestion-accuracy-degradation.md
- docs/delivery/status/work-items/archive/WI-0081.yaml
- docs/delivery/work-items/WI-0116-cluster-assisted-known-person-evidence.md
- docs/delivery/status/work-items/archive/WI-0116.yaml
- src/PhotoIdentity.Core/Clustering/ProvisionalFaceClusterKnownPersonAdvisory.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresProvisionalFaceClusterKnownPersonAdvisoryRepository.cs
- src/PhotoIdentity.Api/ProvisionalFaceClusterEndpoints.cs
- tools/cluster-evaluation/evaluate_suggestions.py
- tools/cluster-evaluation/audit_suggestion_content.py
- tools/cluster-evaluation/evaluate_advisory.py

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
    ./verify-postgres.ps1
