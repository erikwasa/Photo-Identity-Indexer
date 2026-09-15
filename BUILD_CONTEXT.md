# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is proposed, with WI-0110 through WI-0116 complete.**

WI-0116 `Add cluster-assisted known-person advisory evidence` was implemented in PR #338 and accepted by the maintainer on 2026-09-15. The final CI run #1828 passed. `verify-postgres.ps1` passed locally, and the web review confirmed that advisory evidence does not preselect a Person or faces, ordinary per-face evidence remains available, canonical Assigned/Unknown/Rejected totals remain unchanged from merely viewing advisory evidence, and the deferred WI-0115 direct `Make anchor` interaction works as intended.

The accepted advisory policy `m25-cluster-known-person-v1` combines only current exact-model rank-1 suggestion evidence with one current provisional cluster. A qualifying vote must meet the existing ordinary Medium threshold. Strong support requires at least 3 independent exact-content evidence groups for one Person, at least 60% support across independent content groups and at least 60% Core membership. Duplicate exact-content copies cannot inflate support. More than 1 qualifying competing independent vote, more than 20% competing support, or current internal `not same` evidence fails closed to Ambiguous. Advisory evidence is read-only and cannot create canonical assignments.

Private maintainer evaluation produced 41 Strong, 25 Ambiguous and 17 Insufficient clusters. All 41 evaluable Strong proposals were correct: 0 false-person proposals and 100.000% proposal precision. Pure reviewed opportunity recall was 63.077% (41/65). None of the 2 mixed reviewed clusters reached Strong. Estimated review effort was 839 baseline individual actions versus 123 cluster-assisted tasks, for 716 estimated actions saved and 6.82x compression. Only these aggregate findings are recorded; private samples and reports remain uncommitted.

WI-0117 `Evaluate multi-evidence automatic identity assignment` remains proposed and must not start yet. Its declared dependencies are WI-0116 and WI-0081; WI-0116 is complete, but WI-0081 `Investigate degraded identity suggestion accuracy` is still ready/incomplete. WI-0117 explicitly requires WI-0081 to resolve the existing suggestion-accuracy concern before any production automatic-assignment semantics are broadened. Because no M25 work item is currently ready or active, the milestone lifecycle correctly resolves to `proposed`.

PostgreSQL remains the sole writable production catalogue. Provisional clusters and cluster-assisted identity evidence remain derived/non-canonical.

## Next concrete step

If continuing toward M25 automatic-assignment evaluation, start WI-0081 first. Quantify current suggestion accuracy on reviewed catalogue evidence, segment failures and audit reference/ranking behavior before changing thresholds or production suggestion semantics. Only after WI-0081 is accepted should WI-0117 be reassessed and potentially started.

If M25 automation is intentionally deferred, M26 Creative Collections has WI-0118 `Prototype timestamp-first photo moment clustering` ready as an independent next direction.

## Relevant files

- docs/delivery/work-items/WI-0081-suggestion-accuracy-degradation.md
- docs/delivery/status/work-items/active/WI-0081.yaml
- docs/delivery/work-items/WI-0117-multi-evidence-auto-assignment-evaluation.md
- docs/delivery/status/work-items/active/WI-0117.yaml
- docs/delivery/work-items/WI-0116-cluster-assisted-known-person-evidence.md
- docs/delivery/status/work-items/archive/WI-0116.yaml
- src/PhotoIdentity.Core/Clustering/ProvisionalFaceClusterKnownPersonAdvisory.cs
- src/PhotoIdentity.Persistence.Postgres/PostgresProvisionalFaceClusterKnownPersonAdvisoryRepository.cs
- src/PhotoIdentity.Api/ProvisionalFaceClusterEndpoints.cs
- src/PhotoIdentity.Web/Pages/PeopleToIdentify.razor
- tools/cluster-evaluation/evaluate_advisory.py

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
    ./verify-postgres.ps1
