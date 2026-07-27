---
id: M06
title: Evaluation harness
status_source: ../status/milestones.yaml
depends_on: [M05]
---

# M06: Evaluation harness

## Outcome

Detector and identification models can be compared reproducibly using fixed gallery, validation and test sets.

## Work items

- [WI-0017](../work-items/WI-0017-evaluation.md)

## Exit criteria

- Reports include model hashes and pipeline versions.
- Threshold selection uses validation data only.
- Precision, recall, unknown rejection, confusion and throughput are reported.
- Full-archive time and cost can be projected.

## Current work

Pull request [#34](https://github.com/erikwasa/Photo-Identity-Indexer/pull/34) adds the schema-versioned model-lab manifest, deterministic `evaluate` command, validation-only threshold policy, held-out test report, confusion rows, throughput and optional archive projections. GitHub Actions run `30254226939` passed the full repository workflow on the review-ready implementation.

The checked-in fixture is synthetic. Real evaluation manifests, embeddings, reports and identity identifiers remain sensitive local data and must not be committed.

## Deliberate boundaries

M06 measures supplied detector outcomes, embeddings and timings. It does not automatically assemble private datasets from source photos, interpret a threshold as permission to auto-label, or claim that a small local dataset is demographically representative. Those decisions require explicit operator review and later model-selection evidence.
