---
id: M16
title: Face detection recall
status_source: ../status/milestones.yaml
depends_on: [M06]
---

# M16: Face detection recall

## Outcome

The project measures detector recall on a bounded 100-photo sample and improves the detector pipeline only as far as required to meet an explicit product threshold.

## Work items

- [WI-0034](../work-items/WI-0034-detector-recall-baseline.md) — measure the current detector on 100 photos
- [WI-0035](../work-items/WI-0035-yunet-threshold-sweep.md) — tune confidence only when the baseline gate fails
- [WI-0036](../work-items/WI-0036-multiscale-yunet.md) — add multi-scale YuNet only when threshold tuning is insufficient
- [WI-0037](../work-items/WI-0037-detector-candidate.md) — evaluate another detector only when YuNet remains insufficient
- [WI-0038](../work-items/WI-0038-detector-rollout.md) — safely roll out any changed detector pipeline

## Conditional execution

This milestone is intentionally allowed to finish without completing every proposed work item.

- If WI-0034 meets the decision target, cancel WI-0035 through WI-0038 and complete M16.
- If WI-0035 meets the target, cancel WI-0036 and WI-0037, then complete WI-0038.
- If WI-0036 meets the target, cancel WI-0037, then complete WI-0038.
- WI-0037 is required only when the governed YuNet options remain below target.

Cancelled work items mean the evidence showed that the work was unnecessary; they are not failures.

## Decision target

- at least 90% overall recall;
- at least 85% recall in photos with five or more countable faces;
- no more than 10 false or duplicate detections across the 100-photo sample; and
- no material failure category incompatible with the intended archive workflow.

## Exit criteria

- A fixed counting rule and sample-selection method are recorded before measurement.
- Privacy-safe aggregate recall and false-detection evidence is retained.
- The first pipeline meeting the decision target is selected without unnecessary later work.
- Any changed detector pipeline has explicit provenance and a safe canonical-catalogue rollout plan.
