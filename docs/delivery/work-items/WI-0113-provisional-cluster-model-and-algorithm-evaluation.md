---
id: WI-0113
title: Define provisional face clusters and evaluate clustering algorithms
milestone: M25
status_source: ../status/work-items.yaml
depends_on: [WI-0103]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Postgres, PhotoIdentity.ClusterEvaluation, PhotoIdentity.Core.Tests, PhotoIdentity.Persistence.Tests, tools/cluster-evaluation, docs]
---

# WI-0113: Define provisional face clusters and evaluate clustering algorithms

## Objective

Define a model-versioned, regenerable provisional face-cluster contract and select conservative clustering semantics from measured evidence before implementing production cluster review.

## Why

The catalogue contains many unreviewed faces that may strongly resemble one another even when no canonical Person exemplar exists. Density/graph clustering can convert that backlog into candidate identity groups, but an incorrect merge is more damaging than a split. The algorithm and persistence semantics therefore need explicit evaluation rather than an arbitrary global threshold.

## In scope

- Define `provisional face cluster` as derived exact-model evidence, not a canonical Person or assignment.
- Define cluster membership/core/border/noise semantics and how a full deterministic rebuild replaces prior derived cluster state.
- Define stable provenance fields such as embedding model ID/hash, clustering policy/version, generated time and member evidence.
- Define how canonical Assigned, Unreviewed, Unknown and Rejected states participate in discovery/evaluation without being silently rewritten.
- Define negative constraints for obvious conflicts, including manual not-same evidence if introduced and same-photo co-occurrence as supporting conflict evidence where reliable.
- Build a private evaluation/export path that does not commit face crops, embeddings or personal identity data.
- Compare at least DBSCAN, HDBSCAN and a conservative mutual-neighbour/graph approach using the same reviewed sample and exact embeddings.
- Measure false merges, false splits, noise/singleton rate, cluster coverage, cluster-size distribution and sensitivity across age/pose/image-quality variation where the sample allows.
- Select initial production semantics and thresholds/policy only from measured results.
- Document whether PostgreSQL exact/vector-neighbour search is sufficient for the expected archive scale or whether an ANN/index dependency is justified for WI-0114.

## Out of scope

- Production cluster UI.
- Canonical assignment from a cluster.
- Treating cluster IDs as durable person identifiers.
- Publicly storing or committing the private evaluation corpus.

## Acceptance criteria

- [x] The architecture/documentation clearly states that provisional clusters are exact-model derived evidence and are never canonical people.
- [x] A deterministic rebuild contract exists and does not mutate canonical review history.
- [x] Cluster policy/provenance is versionable so results from different embedding/clustering revisions are not silently mixed.
- [x] DBSCAN, HDBSCAN and a conservative graph/mutual-neighbour method are evaluated on the same reviewed sample or an explicit documented reason excludes one.
- [x] Evaluation reports false merges separately and treats them as the primary risk metric rather than optimizing only aggregate cluster coverage.
- [x] Split/noise/coverage trade-offs are reported so the selected algorithm does not hide review workload behind unsafe merges.
- [x] The selected production semantics include an explicit handling strategy for noise/outliers and canonical Unknown faces.
- [x] Private evaluation data and embeddings remain outside the repository.
- [x] The outcome records whether pgvector/ANN is required for WI-0114 or remains optional.

## Implementation status

Implementation is merged in PR #325 (`c25b1a6457185c6b34b20301fa98ed7054ae5063`) and CI run #1755 (`34773907029`) passed. Maintainer verification then passed on 2026-09-13 using a private reviewed exact-model sample; WI-0113 remains `in_review` only until its separate lifecycle closeout PR moves the canonical shard to `completed`.

- Core defines exact-model/policy provenance, Core/Border/Noise member semantics, and the non-canonical cluster boundary.
- PostgreSQL exposes a bounded exact-model reviewed sample without source or display metadata.
- The export tool replaces catalogue identifiers with local sample labels before writing the ignored evaluation file.
- The evaluator compares DBSCAN, HDBSCAN and a conservative mutual-neighbour graph on one shared cosine-distance matrix, with false merges as the primary metric and split/noise/coverage metrics reported separately.
- The architecture document defines deterministic derived-run replacement, canonical review-state precedence, Unknown/noise handling, conflict evidence, and the measured decision gate for WI-0114 neighbour search.
- Core and PostgreSQL tests protect policy validation, exact-model scoping, reviewed-state eligibility, bounded export and deterministic ordering.

## Maintainer verification result

The private sample contained **5,000 reviewed faces**, **107 assigned identity labels**, and **1,294 canonical Unknown faces**. DBSCAN, HDBSCAN and mutual-neighbour candidates were evaluated on the same exact-model cosine-distance matrix. The conservative selected candidate is:

- algorithm: **DBSCAN**,
- `eps`: **0.30**,
- `min_samples`: **3**,
- false-merge pairs: **0 / 443,525**,
- false-merge rate: **0.000000**,
- false-split rate: **0.687251**,
- labelled coverage: **0.542**,
- noise rate: **0.579**,
- same-photo conflicting merges: **0**.

The result intentionally favors merge safety over coverage. The high split/noise rates are accepted for the initial provisional-cluster policy because clusters are advisory derived evidence and false merges are the primary risk; later review work may improve recall without silently weakening this safety boundary.

Age, pose and image-quality variation were **not established** from the available private sample metadata. This is recorded as an evaluation limitation rather than inferred from the embeddings and should be revisited when later cluster-quality review has suitable labelled examples.

Pairwise cosine-distance calculation for 5,000 faces took **0.528 seconds**. The evaluator's quadratic projection for 10,000 faces is **2.111 seconds**. This does not directly benchmark PostgreSQL query execution, but it provides no evidence that an ANN dependency is required at the expected initial scale. WI-0114 should therefore begin with bounded exact PostgreSQL/vector-neighbour retrieval and keep ANN optional; introduce ANN only if measured production incremental retrieval fails the required runtime budget.

Canonical Unknown remains distinct from clustering Noise. Unknown faces may participate as unlabeled points without being assigned; Noise remains a derived outcome and never rewrites canonical review state.

## Verification requirements

Completed 2026-09-13. The maintainer ran the private exact-model exporter and evaluator against the current PostgreSQL catalogue, inspected the aggregate false-merge/split/noise/coverage and same-photo-conflict results, selected the conservative DBSCAN policy above, and recorded exact-first/ANN-optional as the initial WI-0114 neighbour-search decision. Private samples, embeddings, mappings and per-face reports remain uncommitted.
