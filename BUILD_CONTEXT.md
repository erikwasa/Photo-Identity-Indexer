# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is active.**

WI-0110 similar-face discovery, WI-0111 bounded follow-up regeneration, WI-0112 suggested-person grouped review, and WI-0113 provisional-cluster model/algorithm evaluation are complete after implementation, CI, and maintainer verification.

The 2026-09-13 WI-0113 private evaluation used 5,000 reviewed faces, 107 assigned identity labels and 1,294 canonical Unknown faces. It selected DBSCAN with `eps=0.30` and `min_samples=3`: 0/443,525 false-merge pairs, false-split rate 0.687251, labelled coverage 0.542, noise rate 0.579 and zero same-photo conflicting merges. Pairwise cosine distance took 0.528 seconds at 5,000 faces and projects to 2.111 seconds at 10,000 faces. Age/pose/image-quality variation was not established from the available sample metadata and remains a later quality-validation limitation.

The WI-0114 neighbour-search decision is exact-first: begin with bounded exact PostgreSQL/vector-neighbour retrieval and keep ANN optional. Introduce ANN only if measured production incremental retrieval fails the required runtime budget; the private pairwise benchmark is diagnostic, not a direct PostgreSQL latency guarantee.

PostgreSQL remains the sole writable production catalogue. WI-0081 remains the separate suggestion-quality investigation that gates later automatic identity-assignment expansion.

## Next concrete step

Prepare and implement WI-0114: scalable incremental provisional face clustering. Use the selected DBSCAN `eps=0.30` / `min_samples=3` policy as the initial exact-model clustering policy, preserve provisional clusters as derived/non-canonical evidence, and start with bounded exact PostgreSQL/vector-neighbour retrieval.

Do not add ANN unless measured production retrieval justifies it. Preserve the WI-0113 safety boundary: false merges remain the primary risk, canonical Unknown must never be rewritten, and cluster rebuilds must not mutate canonical review history.

## Relevant files

- docs/delivery/work-items/WI-0114-scalable-incremental-face-clustering.md
- docs/delivery/status/work-items/active/WI-0114.yaml
- docs/architecture/provisional-face-clustering.md
- tools/PhotoIdentity.ClusterEvaluation/Program.cs
- tools/cluster-evaluation/evaluate.py
- tools/cluster-evaluation/README.md

## Repository validation

    ./build.ps1
    ./test.ps1
    dotnet run --project tools/PhotoIdentity.Docs -- validate
    dotnet run --project tools/PhotoIdentity.Docs -- generate --check
    ./verify-review.ps1 -Mode Smoke -Configuration Release
