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
