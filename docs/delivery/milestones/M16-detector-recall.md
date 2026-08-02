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

- [WI-0039](../work-items/WI-0039-detector-evaluation-workspace.md) — build reusable photo-level detector evaluation before repeated manual review
- [WI-0034](../work-items/WI-0034-detector-recall-baseline.md) — measure the current detector on 100 photos
- [WI-0035](../work-items/WI-0035-yunet-threshold-sweep.md) — tune confidence only when the baseline gate fails
- [WI-0036](../work-items/WI-0036-multiscale-yunet.md) — add multi-scale YuNet only when threshold tuning is insufficient
- [WI-0037](../work-items/WI-0037-detector-candidate.md) — evaluate another detector only when YuNet remains insufficient
- [WI-0038](../work-items/WI-0038-detector-rollout.md) — safely roll out any changed detector pipeline

## Current implementation decision

The maintainer retained the 50 mechanically selected representative pilot photos and supplemented the difficult half with archive-relevant external photos that cover conditions missing from the original pilot. The exact 100-photo set is staged privately and the isolated YuNet confidence-0.9 batch has completed.

Reviewing individual aligned face crops is not efficient or complete for detector evaluation because it obscures image categories and entirely hides photos with zero detections. M16 therefore adds WI-0039, a reusable, read-only detector evaluation workspace before repeated threshold comparisons. The first implementation slice reads one processing run, lists every photo in stable order, serves the original photo locally and overlays persisted normalized detections. Later slices will import private sample metadata, record face-level ground truth and automatically match later detector runs so the operator reviews only misses, false positives, duplicates and ambiguous matches.

This evaluation data is separate from canonical identity review. Detector judgements must not create person assignments, rejection actions or synthetic identities.

See [M16 detector evaluation workspace status](../status/M16-detector-evaluation-workspace.md).

## Conditional execution

This milestone is intentionally allowed to finish without completing every proposed work item.

- WI-0039 must provide the reusable review path before WI-0034 completes.
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

## Follow-on requirements

A detector change can add a materially harder face population: small faces, profiles, partial occlusion, blur, low light and people in the background. When the accepted detector pipeline materially changes which faces enter the catalogue, rerun the exact-model embedding comparison using the same new detections, aligned crops and deterministic evaluation split for every embedder before reaffirming the model recommendation.

The pilot also records an optional count of correctly detected faces that appear to be background people or people the operator does not know. This is a workload estimate, not an identity decision. Later review design should support explicit non-identity outcomes such as `Unknown person`, `Background / ignore`, `Not a face` and `Deferred`, so every real face does not have to become a named person. Those decisions must remain auditable and reversible.

## Exit criteria

- A fixed counting rule and sample-selection method are recorded before measurement.
- The photo-level workspace shows the full source image and every persisted detector box, including photos with no detections.
- Private source-group and category metadata can be applied consistently across repeated detector runs.
- Reusable face-level ground truth prevents full manual recounting for every threshold or detector candidate.
- Privacy-safe aggregate recall, false-detection and likely-background evidence is retained.
- The first pipeline meeting the decision target is selected without unnecessary later work.
- Any changed detector pipeline has explicit provenance and a safe canonical-catalogue rollout plan.
- A materially expanded face population triggers a fresh exact-model comparison before production model selection.
