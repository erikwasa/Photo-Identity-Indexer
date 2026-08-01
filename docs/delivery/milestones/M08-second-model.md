---
id: M08
title: Multi-model local evaluation
status_source: ../status/milestones.yaml
depends_on: [M06, M07]
---

# M08: Multi-model local evaluation

## Outcome

At least one additional detector or embedder runs locally through the same neutral contracts, coexists with the baseline in the same catalogue and is compared on the same reviewed corpus.

## Work items

- [WI-0019](../work-items/WI-0019-second-model.md)
- [WI-0030](../work-items/WI-0030-multi-model-comparison.md)

## Exit criteria

- Baseline and candidate model outputs coexist by explicit model ID and hash.
- Existing people, human labels and review history remain model-independent and unchanged.
- The same 500-image corpus and evaluation splits are used for both models.
- The review application can distinguish model revisions without mixing their suggestions.
- Accuracy, unknown rejection, detections, confusion, throughput and storage are compared side by side.
- Licence and model-governance evidence is recorded for every candidate.

## Completion

Completed on 2026-08-01.

The pinned `sface-2021dec-int8` candidate was processed and evaluated beside the `sface-2021dec-fp32` baseline on the same accepted private source scope, detector configuration and deterministic split. Exact-model outputs coexisted without changing canonical people, labels or review history.

A private manual review of 20 representative faces found both revisions correct in every case and no material practical difference. The recommendation is to retain the FP32 baseline as the current default. The INT8 revision remains available as a governed candidate for later deployment-cost or throughput evidence, while final production selection remains deferred to M11.

Only privacy-safe aggregate conclusions are retained in Git. Private media, identities, databases, manifests, reports, paths and the detailed review record remain local.
