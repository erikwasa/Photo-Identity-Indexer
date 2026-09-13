---
id: WI-0113
title: Define provisional face clusters and evaluate clustering algorithms
milestone: M25
status_source: ../status/work-items.yaml
depends_on: [WI-0103]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests, docs]
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

- [ ] The architecture/documentation clearly states that provisional clusters are exact-model derived evidence and are never canonical people.
- [ ] A deterministic rebuild contract exists and does not mutate canonical review history.
- [ ] Cluster policy/provenance is versionable so results from different embedding/clustering revisions are not silently mixed.
- [ ] DBSCAN, HDBSCAN and a conservative graph/mutual-neighbour method are evaluated on the same reviewed sample or an explicit documented reason excludes one.
- [ ] Evaluation reports false merges separately and treats them as the primary risk metric rather than optimizing only aggregate cluster coverage.
- [ ] Split/noise/coverage trade-offs are reported so the selected algorithm does not hide review workload behind unsafe merges.
- [ ] The selected production semantics include an explicit handling strategy for noise/outliers and canonical Unknown faces.
- [ ] Private evaluation data and embeddings remain outside the repository.
- [ ] The outcome records whether pgvector/ANN is required for WI-0114 or remains optional.

## Verification requirements

Reproducible local evaluation tooling/tests plus a documented private reviewed-sample result sufficient to justify the selected clustering semantics.
