# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is active.**

WI-0110 similar-face discovery is complete. WI-0111 bounded follow-up regeneration, WI-0112 suggested-person grouped review, and WI-0113 provisional-cluster model/algorithm evaluation are implemented/merged and remain `in_review` pending one combined maintainer acceptance session.

WI-0113 merged in PR #325 with CI run #1755 green. It defines provisional clusters as exact-model, policy-versioned derived evidence, adds a bounded PostgreSQL reviewed-sample exporter with private pseudonymized output, and adds local DBSCAN/HDBSCAN/mutual-neighbour evaluation tooling. No production clustering threshold or ANN choice may be recorded until the maintainer runs the private reviewed sample.

PostgreSQL remains the sole writable production catalogue. WI-0081 remains the separate suggestion-quality investigation that gates later automatic identity-assignment expansion.

## Next concrete step

Run the combined maintainer verification for WI-0111, WI-0112, and WI-0113 against the current PostgreSQL catalogue.

- WI-0111: make a qualifying manual identity assignment without pressing Regenerate; observe automatic `queued` -> `running` -> `current`, verify review remains responsive, verify no recursive second run, then restart with automatic follow-up disabled and confirm explicit regeneration still works.
- WI-0112: open `Suggested groups`, choose the production exact model, review a person with multiple pending suggestions, accept only a subset, leave/remove an exception, reject one incorrect face-person suggestion from Details, confirm group counts update, and repeat the key subset-selection flow on mobile/touch.
- WI-0113: export a private reviewed exact-model sample, run DBSCAN/HDBSCAN/mutual-neighbour evaluation, inspect false merges and same-photo conflicts first, record the conservative selected policy plus age/pose/image-quality observations, and decide from measured local timing whether WI-0114 should use exact PostgreSQL/vector-neighbour retrieval or introduce ANN.

Do not mark any of WI-0111/WI-0112/WI-0113 completed until their maintainer evidence is recorded. Do not start WI-0114 until the WI-0113 production-policy and exact-vs-ANN decision are recorded.

## Relevant files

- docs/delivery/work-items/WI-0111-event-driven-match-regeneration.md
- docs/delivery/work-items/WI-0112-suggested-person-review-workspace.md
- docs/delivery/work-items/WI-0113-provisional-cluster-model-and-algorithm-evaluation.md
- docs/architecture/provisional-face-clustering.md
- src/PhotoIdentity.Web/Pages/MatchRegeneration.razor
- src/PhotoIdentity.Web/Pages/SuggestedPersonGroups.razor
- tools/PhotoIdentity.ClusterEvaluation/Program.cs
- tools/cluster-evaluation/evaluate.py
- tools/cluster-evaluation/README.md

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
