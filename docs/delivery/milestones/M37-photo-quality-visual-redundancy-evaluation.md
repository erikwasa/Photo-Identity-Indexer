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

## Integration

M37's benchmark and frozen WI-0193 research results were integrated into `main` in PR #585. The local-file tracking cleanup followed in PR #586. All subsequent M37 workitem pull requests target `main`; WI-0194 and WI-0195 remain open, so M37 is not complete. The WI-0193 held-out photo-quality study is consumed and must not be rerun or tuned.

## Delivery sequence

1. WI-0192 establishes the representative human-labelled benchmark and reusable review/export foundation.
2. WI-0193 and WI-0194 may proceed independently once WI-0192 is complete.
3. WI-0195 combines only accepted evaluation outputs in a non-production collection simulation and decides whether production follow-up work is justified.

## Progress through 2026-10-18

WI-0192 benchmark accepted and WI-0193 quality research concluded: frozen TOPIQ selective pairwise preferences passed 80%/20%/80% held-out *point-estimate* criteria, with overall ordering 95% photo-bootstrap interval extending below 80%. **No absolute quality classifier, automatic Very Low exclusion, or production model was approved.** WI-0194 archive-wide redundancy evidence and WI-0195 collection simulation remain pending. Never reuse the consumed WI-0193 held-out labels for alternative model or threshold selection.

## M37 governance after completed WI-0194 human studies (2026-10-20)

WI-0194 has now labelled 200 calibration and 180 guarded held-out visual pairs. The observed pair-level dHash0–8/high-CLIP candidate rates were promising but none qualified for **automatic suppression** given selected pair populations, correlated photos, holdout quarantine and inadequate hard-disagreement coverage. The formal result is **NO-GO / DEFER** on all automatic redundancy suppression and combination arms. WI-0193 concluded that the frozen TOPIQ p75 pairwise ordering was a **research-level selective preference only**, with no absolute classifier, Very Low exclusion or production-license gate acceptance.

The existing WI-0195 four-arm acceptance **cannot be represented as passed or safely activated**. One optional read-only, offline **shadow-simulation research probe** has been implemented to measure real Creative preview score-coverage and the observed effect of the frozen TOPIQ rule only **within identical relevance-score and candidate-kind contiguous tiers**, while baseline remains unchanged. This limited experiment does not lift the WI-0195 dependency for production, use held-out human quality/redundancy labels, or start a new model. The original acceptance criteria remain pending. Even a favorable A/B preference would require separately approved broader-quality evidence, license/deployment and full-archive score coverage before product integration.

## Final research-evaluation closure path (2026-10-20)

See [M37 decision ledger](M37-decision-ledger.md) for the authoritative
evidence, explicit GO/NO-GO/DEFER policy matrix, remaining WI-0195 bounded
collection comparison and closure plan. The accepted WI-0192 benchmark and
WI-0193 frozen quality research are complete. WI-0194 archive-scale candidate
retrieval and 200+180 calibration/held-out pair reviews found promising
candidate classes but **rejected all production automatic redundancy
suppression**. No absolute quality rating, Very Low exclusion, or combined
automatic selection policy was approved. These **NO-GO outcomes are valid
research decisions**, not missing engineering to be silently optimized.

The maintainer's first WI-0195 real Creative Collection trial passed
8/8 tests but had **0/80 candidates covered** by 600 old IQA scores.
The final bounded research tranche maps existing current local proxies
and **defaults to no-inference planning**. Optional model use is explicit
and limited to 120 new photos with third-party rights/weight cautions; then
the *same frozen* relevance-safe quality shortlist comparator and existing
blind A/B viewer can assess real maintained collection experience once.
After that single bounded phase, record four explicit policy outcomes
and either **close M37 as an evidence-based research rejection/defer**
(with the original unachieved four-arm goals documented) or identify an
independently justified, separately scoped follow-up outside this
milestone. Do not indefinitely extend WI-0194/0195 with single-command
micro-checkpoints.

## Clarified intended product use: suggestions with manual control (2026-10-20)

The maintainer wants **automatic low-quality/unwanted-photo suggestions that
they can evaluate and exclude manually**, with opt-in decisions **per
use case**. This is less risky than the originally proposed automatic Very
Low exclusion and automatic redundant-photo suppression, which remain
NO-GO under existing evidence. The old 98%+ automatic-exclusion precision
gate does not apply unchanged to a *human-reviewed candidate shortlist*;
measure review burden, suggestions accepted, useful discoveries and harm
avoided instead. Avoid automatically hiding photos from Search, Smart,
Creative, Archive or slideshows.

The existing manual 'Exclude from Photo Identity' action is a **source-copy
scoped local purge** (ADR-0008): it leaves the OneDrive original intact but
removes Photo Identity's retained derivatives and identity/metadata evidence;
it is not a reversible soft hide. A review suggestion must be kept separate
from the explicit operator-confirmed purge. Per-collection/slideshow
reversible omission would be a different future action with separate opt-in.
Track the product implementation in
[issue #629](https://github.com/erikwasa/Photo-Identity-Indexer/issues/629).

For M37, complete the bounded real-collection quality experiment, and
record **manual suggestion feasibility separately** from the original four
automatic-policy decisions. Do not claim manual review/product exclusion
support is delivered merely by a research scorer; end-user workflow belongs
in an explicitly accepted follow-up.

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

## Final acceptance reconciliation — 2026-10-21

**Completed research evaluation; original production-policy aspiration
NOT achieved.** Accepted benchmark/review design, blinded heldout
quality evidence, archive-wide visual redundancy candidate search,
and explicit adoption/go-no-go decisions are evidenced in
[M37 final decision ledger](M37-decision-ledger.md).
The original WI-0195 proposed fully populated four-arm production-style
simulation is **not demonstrated**: a real 80-photo shortlist had
0 overlapping pre-existing TOPIQ scores; a subsequent bounded
80/80 current-proxy score *plan* passed, but no operator consent
was given to load any additional third-party weights. Unattended
Very Low exclusion, visual redundancy suppression, and their
combination are explicit **NO-GO**, not suppressed behind an
unproven heuristic. Research-level TOPIQ selective ordering
is **DEFER for product integration**. Any original criteria
requiring a four-arm validated *automatic* selection experiment
remain intentionally **unmet**, not silently checked off.

The operator's separate human-in-the-loop aim did pass:
a bounded companion queue generated 129 technical suggestions;
the maintainer reviewed all (72 Keep, 37 Dismiss, 20 Consider)
and confirmed most considered cases genuinely unwanted and review
reasonably convenient. Issue [#629](https://github.com/erikwasa/Photo-Identity-Indexer/issues/629)
owns the resulting native manual-review product, including
merged PR #633 and its pending maintained-app smoke test.
The native UI is **not** part of the WI-0195 four-arm acceptance
and has no automatic exclusion or collection/slideshow filtering.

**M37 marked completed as a decision-oriented research milestone**;
WI-0194 completed with **suppression NO-GO**, WI-0195
cancelled with the original four-arm acceptance explicitly
unmet. This is the final boundary between research and
operator-facing product work.
