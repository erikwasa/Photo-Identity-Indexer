---
id: M37
title: Photo quality and visual redundancy evaluation
status_source: ../status/milestones.yaml
depends_on: [M26]
---

# M37: Photo quality and visual redundancy evaluation

## Outcome

Photo Identity has measured evidence for two independent derived signals needed by future collection creation: an absolute per-photo quality rating and archive-wide visual redundancy. The milestone ends with a collection simulation and explicit go/no-go decisions before either signal changes production selection.

Quality is evaluated as an intrinsic rating of one immutable photo revision, not as a percentile or comparison against the rest of the archive. Visual redundancy is evaluated separately as a relationship between photos and must not depend on capture date, people, folder, event or location. The signals meet only in WI-0195, where redundant candidates may use the independent quality rating to choose a representative.

The deterministic 300-photo WI-0182 run remains useful evidence that the first hand-written proxy thresholds were inadequate, but maintainer review found that sample insufficiently representative of the archive. M37 therefore builds a new stratified benchmark rather than treating those 300 photos as ground truth for model selection.

## Delivery principles

- Keep absolute quality and visual redundancy as separate, versioned derived evidence.
- Define quality levels independently of archive composition so a photo does not change quality merely because other photos are added.
- Use a stratified archive sample plus deliberate challenge cases; do not rely on stable-hash/random convenience sampling alone.
- Keep human quality labels blinded from model scores/reasons and freeze calibration versus held-out partitions before tuning.
- Prioritize precision over recall for any future Very Low / normally-excludable quality tier; false exclusion is more costly than leaving a bad photo available.
- Evaluate visual redundancy across the archive without same-date, same-person, same-folder or same-moment restrictions.
- Keep exact duplicates, visual redundancy and semantic similarity as distinct concepts.
- Prefer false-negative redundancy over false-positive suppression: two repetitive photos are less harmful than suppressing a meaningfully different one.
- Compare review-proxy input with original-pixel input on a bounded subset before choosing the eventual one-time scoring source.
- Do not persist production quality ratings/visual embeddings or change Search/Smart/Creative semantics in this milestone.
- Keep originals immutable and keep private photos, paths, model outputs and human labels out of Git.
- Measure runtime, model size, storage and exact-search practicality before choosing production persistence or ANN infrastructure.

## Work items

- [WI-0192](../work-items/WI-0192-representative-photo-quality-benchmark.md) - build the representative archive sample, blinded five-level quality review workflow and frozen calibration/held-out split.
- [WI-0193](../work-items/WI-0193-absolute-photo-quality-model-evaluation.md) - benchmark and calibrate absolute per-photo quality estimators, including the safety of a Very Low tier.
- [WI-0194](../work-items/WI-0194-archive-wide-visual-redundancy-evaluation.md) - benchmark archive-wide visual redundancy embeddings and conservative suppression thresholds.
- [WI-0195](../work-items/WI-0195-quality-aware-collection-simulation.md) - simulate collection creation with quality preference and visual redundancy suppression, then record adoption decisions.

## Delivery sequence

1. WI-0192 establishes the representative human-labelled benchmark and reusable review/export foundation.
2. WI-0193 and WI-0194 may proceed independently once WI-0192 is complete.
3. WI-0195 combines only accepted evaluation outputs in a non-production collection simulation and decides whether production follow-up work is justified.

## Exit criteria

- [ ] The quality benchmark is demonstrably broader than WI-0182's deterministic 300-photo sample and covers representative archive strata plus model-disagreement/challenge cases.
- [ ] Human quality labels use Very Low / Low / Normal / High / Very High / Unsure without exposing candidate model results during review.
- [ ] A frozen held-out partition is used for final quality evidence, with visually near-identical photos kept in the same partition.
- [ ] At least the WI-0182 baseline and multiple modern no-reference IQA candidates are compared with runtime/deployment evidence.
- [ ] Any recommendation for a Very Low automatic exclusion tier is supported by extremely high held-out precision; otherwise automatic exclusion is explicitly rejected or deferred.
- [ ] Archive-wide redundancy evaluation distinguishes Redundant from Similar-but-keep-both and semantic similarity, independent of date/person/location constraints.
- [ ] Any recommended automatic redundancy threshold achieves conservative held-out precision and has measured archive-scale runtime/storage/query cost.
- [ ] Real collection scenarios compare current behavior, quality preference, similarity suppression and the combined policy without mutating production collections.
- [ ] The milestone records separate go/no-go decisions for absolute quality, Very Low exclusion, visual redundancy and combined collection use.
- [ ] Any production persistence, Search/Smart/Creative behavior or UI change is created as separate follow-up work after the evaluation.
