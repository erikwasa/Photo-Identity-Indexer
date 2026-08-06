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
- [WI-0040](../work-items/WI-0040-detector-comparison-review-workspace.md) — keep the complete comparison image and its decisions visible in a viewport-fitted review workspace
- [WI-0034](../work-items/WI-0034-detector-recall-baseline.md) — measure the current detector on 100 photos
- [WI-0035](../work-items/WI-0035-yunet-threshold-sweep.md) — determine whether confidence tuning is sufficient
- [WI-0036](../work-items/WI-0036-multiscale-yunet.md) — evaluate full-image plus tiled YuNet because threshold tuning was insufficient
- [WI-0037](../work-items/WI-0037-detector-candidate.md) — qualify and evaluate another detector after multi-scale YuNet remained insufficient
- [WI-0038](../work-items/WI-0038-detector-rollout.md) — safely roll out any changed detector pipeline

## Current implementation decision

The maintainer retained the 50 mechanically selected representative pilot photos and supplemented the difficult half with archive-relevant external photos that cover conditions missing from the original pilot. The exact 100-photo set remains staged privately and immutable across detector runs.

The reusable detector-evaluation workspace was delivered through pull requests #70, #71, #72 and #74. It shows complete source photos including zero-detection cases, imports private source-group/category metadata, persists resumable ground-truth sessions, supports source-pixel zoom, freezes reusable face-level ground truth, validates isolated candidate catalogues and exports comparison summaries and the M16 gate.

Pull requests #76 and #77 refined comparison review to one photo at a time, replaced internal comparison terminology with operator-facing decisions, added clear status treatment, introduced compact numbered reference/candidate markers and automatically classified candidate-free reference faces as detector misses.

WI-0040 was completed through PRs #79 and #80. The delivered workspace fits the complete image and decisions into a stable viewport-bounded review surface, keeps decision overflow independent from the image, provides zoom and pan inspection, resets transient view state between photos, links image markers with decision controls and keeps comparison images readable across isolated catalogue switches when staged filename and full SHA-256 match.

The confidence-0.9 YuNet baseline failed the predeclared M16 decision target on 2026-08-05. The maintainer then completed isolated confidence `0.8`, `0.7`, `0.6` and `0.5` comparisons against the same frozen face-level ground truth by 2026-08-06. Every governed threshold failed the complete M16 gate. Detailed counts and category evidence remain private.

WI-0036 delivered an opt-in full-image plus deterministic overlapping-tile YuNet pipeline through PR #82. The maintainer completed the governed private comparisons on 2026-08-07:

- multi-scale confidence `0.9` failed the complete gate, although it performed better than the single-pass confidence-0.9 baseline and single-pass confidence `0.8`; and
- multi-scale confidence `0.7` returned more than 100 false or duplicate detections, far above the maximum of 10.

Confidence `0.6` was intentionally not run because a lower threshold could not plausibly repair the already disqualifying false/duplicate workload. No YuNet threshold or multi-scale configuration is approved for rollout.

WI-0036 is complete and WI-0037 is active. CenterFace ONNX is the first qualification target because it provides five landmarks and a direct compact ONNX artifact without the explicit non-commercial pretrained-weight restriction attached to the higher-ranked SCRFD option. Exact artifact provenance, model-weight interpretation, WIDER FACE limitations, tensor semantics and Windows runtime compatibility must be pinned before the private sample is processed.

This evaluation data remains separate from canonical identity review. Detector judgements must not create person assignments, rejection actions or synthetic identities.

See [M16 detector evaluation workspace status](../status/M16-detector-evaluation-workspace.md).

## Conditional execution

This milestone is intentionally allowed to finish without completing every proposed detector-pipeline work item.

- WI-0034 established that confidence `0.9` does not meet the decision target.
- WI-0039 completed reusable candidate-run matching and summaries in PR #74.
- WI-0040 completed the viewport-fitted and cross-catalogue-safe comparison-review workflow in PRs #79 and #80.
- WI-0035 established that every governed single-pass confidence from `0.9` through `0.5` fails the complete gate.
- WI-0036 established that multi-scale confidence `0.9` still fails and that lowering multi-scale confidence to `0.7` creates an unacceptable false/duplicate workload.
- WI-0037 is now required and qualifies a different detector family against the same immutable sample and ground truth.
- WI-0038 remains blocked until WI-0037 identifies an acceptable pipeline.

Cancelled or skipped candidates mean the evidence showed that further work was unnecessary; they are not failures of the evaluation process.

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
- Comparison exceptions can be reviewed in a stable viewport-fitted workspace without page-level back-and-forth scrolling during the normal photo-to-photo loop.
- Saved comparison images remain resolvable across isolated catalogue switches when verified source bytes are available.
- Privacy-safe aggregate recall, false-detection, runtime, review-effort and likely-background evidence is retained.
- The first pipeline meeting the decision target is selected without unnecessary later work.
- Any changed detector pipeline has explicit provenance and a safe canonical-catalogue rollout plan.
- A materially expanded face population triggers a fresh exact-model comparison before production model selection.
