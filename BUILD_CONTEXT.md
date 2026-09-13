# Build context

This file is intentionally a short handoff for the next development or verification session. Formal work-item lifecycle status is resolved through `PhotoIdentity.Docs`.

## Current focus

**M25 Face discovery and cluster-assisted identity review is active.**

WI-0110 similar-face discovery, WI-0111 bounded follow-up regeneration, and WI-0112 suggested-person grouped review are complete. WI-0113 provisional-cluster model/algorithm evaluation has passed its private reviewed-sample verification and remains `in_review` only until its separate one-work-item closeout PR moves it to `completed`.

WI-0113 merged in PR #325 with CI run #1755 green. The 2026-09-13 private evaluation used 5,000 reviewed faces, 107 assigned identity labels and 1,294 canonical Unknown faces. It selected DBSCAN with `eps=0.30` and `min_samples=3`: 0/443,525 false-merge pairs, false-split rate 0.687251, labelled coverage 0.542, noise rate 0.579 and zero same-photo conflicting merges. Pairwise cosine distance took 0.528 seconds at 5,000 faces and projects to 2.111 seconds at 10,000 faces. Age/pose/image-quality variation was not established from the available sample metadata and remains a later quality-validation limitation.

The WI-0114 neighbour-search decision is exact-first: begin with bounded exact PostgreSQL/vector-neighbour retrieval and keep ANN optional. Introduce ANN only if measured production incremental retrieval fails the required runtime budget; the private pairwise benchmark is diagnostic, not a direct PostgreSQL latency guarantee.

PostgreSQL remains the sole writable production catalogue. WI-0081 remains the separate suggestion-quality investigation that gates later automatic identity-assignment expansion.

## Next concrete step

Merge the WI-0112 closeout PR, then close WI-0113 through its own lifecycle PR. After WI-0113 is completed, prepare WI-0114 using the selected DBSCAN policy and exact-first neighbour-search decision.

Do not add ANN to WI-0114 unless measured production retrieval justifies it.

## Relevant files

- docs/delivery/work-items/WI-0113-provisional-cluster-model-and-algorithm-evaluation.md
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
