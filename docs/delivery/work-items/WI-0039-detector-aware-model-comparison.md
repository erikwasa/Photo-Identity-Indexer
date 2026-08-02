---
id: WI-0039
title: Rerun model comparison after detector expansion
milestone: M16
status_source: ../status/work-items.yaml
depends_on: [WI-0038, WI-0030]
affected_modules: [Evaluation, Model lab, Documentation]
---

# WI-0039: Rerun model comparison after detector expansion

## Objective

Rerun the exact-model embedding comparison when the accepted detector pipeline materially changes which faces enter the catalogue.

## Rationale

A higher-recall detector is expected to add harder faces: smaller people, profiles, partial occlusion, blur, low light and background people. The previous FP32-versus-INT8 conclusion was valid for the previous face population, but it must not be assumed to cover a materially different population.

## Scope

- Freeze the selected detector pipeline identity, configuration and exact detected face set.
- Generate aligned crops and embeddings for every compared embedder from that same face set.
- Reuse deterministic gallery, validation and held-out split rules.
- Report results by detector-recall category, including small and likely background or unknown faces.
- Review unknown-person false acceptance and representative disagreements manually.
- Preserve canonical people, assignments, rejections and append-only review history.

## Acceptance criteria

- [ ] Every embedder receives the same source revisions, detections, aligned crops and evaluation split.
- [ ] Exact detector and embedder IDs, hashes and pipeline configuration are recorded.
- [ ] Results include top-one accuracy, unknown-person false acceptance, runtime and category breakdowns.
- [ ] The accepted recommendation is based on the expanded face population rather than copied from the earlier comparison.
- [ ] No suggestion score creates a canonical identity automatically.
- [ ] Only privacy-safe aggregate conclusions are committed.

## Conditional gate

Cancel this work item when WI-0034 proves that no detector-pipeline change is required and the evaluated face population remains materially unchanged. Otherwise WI-0039 is part of the M16 exit gate.
