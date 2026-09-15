---
id: WI-0081
title: Investigate degraded identity suggestion accuracy
milestone: M21
status_source: ../status/work-items.yaml
depends_on: [WI-0016, WI-0043]
related_adrs: [ADR-0006]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Worker, PhotoIdentity.Web]
---

# WI-0081: Investigate degraded identity suggestion accuracy

## Priority

**Medium.** Investigate after the critical synchronization issue and the high-priority detected-face clarity issue unless new evidence raises the severity.

## Problem statement

The maintainer reports that **identity suggestion accuracy is getting worse** as the real catalogue/review corpus grows.

The symptom was initially qualitative rather than a quantified regression. Before changing thresholds, ranking, representative embeddings or models, Photo Identity needs evidence that identifies whether the degradation comes from data quality, reference-set growth, incorrect/weak identity evidence, model behavior, ranking policy, face quality, score calibration, or another source.

## Investigation objective

Measure the regression on representative reviewed data, segment the failures, audit the suggestion/reference pipeline, and determine the dominant cause(s) before selecting a mitigation.

Do not compensate by simply lowering/raising a threshold or changing model settings without comparative evidence. A change that increases top-1 accuracy for one subset must not silently worsen false-positive risk or previously verified review semantics elsewhere.

## Investigation questions

### Measurement and drift

- Can current top-1/top-k suggestion accuracy be measured from already reviewed/confirmed catalogue evidence without leaking private biometric data outside the local machine?
- Has accuracy actually declined over time, or has the queue composition shifted toward harder faces as easy cases are reviewed first?
- Does degradation correlate with processing run, suggestion model revision, confidence band, face size/quality, age of photo, pose, occlusion or number of known identities?
- Are a small number of identities responsible for disproportionate confusion?

### Reference/evidence quality

- Are incorrect confirmed assignments, accidental manual labels, duplicates or merged identities contaminating reference data?
- Are low-quality, tiny, blurred, profile or partially occluded faces contributing equally to identity references when they should not?
- Does each person accumulate so many embeddings that nearest-neighbor behavior becomes noisier or biased?
- Does featured/representative-photo selection affect suggestions, or is suggestion reference construction independent of presentation choices as intended?
- Are multiple faces from one photo/person overrepresented relative to a more diverse identity reference set?

### Model and ranking behavior

- Is the currently active embedding/model revision still the same one used for prior quality expectations?
- How do genuine-match and impostor score distributions look now compared with earlier evaluation data?
- Are current score thresholds/calibration still appropriate as the identity population grows?
- Would reference curation, per-person prototypes/centroids, quality weighting, diversity sampling, score normalization or a different ranking strategy improve results without hiding uncertainty?
- Is a model change justified, or can the issue be addressed in the reference/ranking layer?

## Safety and semantic constraints

- Keep embeddings, faces and private identity data local unless an accepted architecture explicitly permits otherwise.
- Do not rewrite confirmed identity history merely to improve an aggregate metric.
- Preserve auditable provenance for suggestions and assignments.
- False-positive risk matters more than making every face receive a confident suggestion.
- Any proposed mitigation must be evaluated against both accuracy and abstention/uncertainty behavior.

## Investigation workflow

The implementation extends the existing privacy-safe export rather than adding a second source of identity truth. `PhotoIdentity.ClusterEvaluation` keeps the backward-compatible `faces` array as the bounded exact-model reviewed target sample used by existing cluster evaluators. For WI-0081 it additionally exports `referenceFaces`, containing the same confirmed reference population used by production matching: current reviewed assignments plus legacy confirmed labels that have no review action, with merged People excluded.

Person, face, photo and exact-content identifiers are replaced with deterministic local labels shared across both arrays. The export also records the exact persisted suggestion policy, detector confidence, normalized face area, review/reference time and whether the current Person has merge history. Person names, source paths, crop paths, raw catalogue identifiers and policy actor data are not exported.

`tools/cluster-evaluation/evaluate_suggestions.py` evaluates the sample locally without writing production suggestions or assignments. It ranks each reviewed target against `referenceFaces` using production's best-exemplar-per-Person aggregation, removing the target itself when it is also a reference. Historical target-specific rejected-Person filters are intentionally not replayed: the holdout measures identity/reference evidence rather than reconstructing past interaction state.

The primary accuracy baseline is duplicate-resistant leave-one-out ranking, which additionally excludes every reference from the target's exact-content group. Reviewed identities that have no remaining same-Person holdout reference are reported separately rather than counted as matching failures.

The report covers top-1/top-3/top-5 accuracy, current High/Medium policy behavior, conservative suggestion emission on reviewed Unknown targets, genuine-vs-best-impostor score distributions and overlap, merge-history representation, reference concentration, and segmentation by detector confidence, normalized face area, true-Person reference-set size and review chronology. Model revisions are evaluated separately by exact model hash rather than pooling incompatible evidence.

Two non-model reference/ranking mitigation families are compared offline against the duplicate-resistant current ranking: a normalized per-Person centroid and a bounded quality/diversity-selected reference set. Before/after deltas include top-1 accuracy, High precision/coverage and reviewed-Unknown High emission so an apparent accuracy gain cannot silently trade for greater false-positive risk.

Exact source-copy duplication is audited separately by `audit_suggestion_content.py`. `contentGroup` is an image-level exact-content identifier, so several faces from one ordinary group photo legitimately share it. The corrected audit only treats exact content as repeated when one `contentGroup` spans multiple pseudonymized `photoGroup` values. This supersedes the original report's `duplicateReferenceContentGroups`, `crossLabelDuplicateReferenceGroups`, and `assignedUnknownMixedTargetContentGroups` fields for contamination conclusions.

This investigation does **not** change production thresholds, reference construction, ranking, auto-assignment or review semantics.

## Private evaluation findings — 2026-09-15

The maintainer ran the exact-model private evaluator against the current PostgreSQL catalogue using `sface-2021dec-fp32` / `0ba9fbfa01b5270c96627c4ef784da859931e02f04419c829e83484087c34e79`.

Evaluation population:

- 14,469 reviewed targets, including 4,116 reviewed Unknown targets.
- 10,353 confirmed production references across 160 identities.
- 10,316 known targets had a usable duplicate-resistant holdout reference; 37 known targets had none and were excluded from top-k denominators.

Primary baseline and policy behavior:

- Duplicate-resistant max-exemplar top-1/top-3/top-5 accuracy: **96.491% / 98.468% / 98.924%**.
- Production-reference max-exemplar top-1 was **96.646%**, only 0.155 percentage points above the duplicate-resistant baseline. Exact-image-content leakage therefore has a small effect on top-1 accuracy in this sample, although it raises High coverage from 49.709% to 53.228%.
- Duplicate-resistant conservative High precision was **99.844%** with **49.709%** known-target High coverage.
- Reviewed Unknown High emission was **0.121%** (approximately five of 4,116 Unknown targets), so the current High policy remains strongly conservative.

The strongest measured accuracy correlation is face quality:

- Detector confidence `<0.70`: 1,412 evaluable known targets, **86.048%** top-1 (197 errors), while High suggestions in this segment were 357/357 correct.
- Detector confidence `0.70-0.85`: **96.507%** top-1.
- Detector confidence `0.85-0.95`: **99.334%** top-1.
- Detector confidence `>=0.95`: **100.000%** top-1 on 534 evaluable targets.
- Face area `<0.5%` of the image: 2,187 evaluable targets, **91.084%** top-1 (195 errors).
- Face area `0.5-2%`: **96.899%** top-1; `2-8%`: **98.233%**; `>=8%`: **98.812%**.

Sparse identity evidence is also disproportionately difficult, but affects a much smaller population:

- One usable holdout reference: 36 targets, **83.333%** top-1.
- Two to four: 135 targets, **71.852%** top-1.
- Five to nine: 132 targets, **81.061%** top-1.
- Ten or more: 10,013 targets, **97.074%** top-1.
- The 303 evaluable targets with fewer than ten usable references are only 2.94% of the evaluable known sample but account for 69 of the 362 duplicate-resistant top-1 errors (about 19%). A further 37 known targets have no usable holdout reference at all.

Review chronology does not show monotonic degradation as the catalogue grows:

- Oldest quartile: **93.089%** top-1.
- Middle-old: **97.601%**.
- Middle-new: **98.877%**.
- Newest quartile: **96.368%**.

The newest quartile is materially harder than the two preceding quartiles (lower top-1, lower median score/margin and much lower High coverage), which is consistent with a recent shift toward harder review material. Because the oldest quartile is worse still, this does **not** support a simple monotonic matcher-decay explanation tied to catalogue size alone.

Score behavior also supports retaining a margin-aware confidence policy:

- Genuine score median / p05 / p95: **0.7598 / 0.5152 / 0.9390**.
- Best-impostor median / p05 / p95: **0.4584 / 0.3548 / 0.6458**.
- Best impostor outranks or ties genuine for **3.509%** of evaluable known targets.
- **79.604%** of best-impostor scores are at or above the Medium score threshold, while only **1.454%** reach the High score threshold. A raw Medium score threshold alone is therefore not sufficiently discriminative; ranking/margin evidence remains important.

The two tested reference-reduction alternatives should not replace the current matcher in their evaluated form:

- Per-Person centroid: top-1 **90.442%** (-6.049 pp), High coverage -24.932 pp, High precision -0.117 pp, Unknown High emission +0.024 pp.
- Quality/diversity cap of 8: top-1 **63.620%** (-32.871 pp), High coverage -45.211 pp and High precision -0.915 pp. The small reduction in Unknown High emission does not justify the severe known-person accuracy loss.

Reference population is highly skewed (median 5 references per identity, maximum 2,869; largest 10% of identities hold 86.632% of references), but the current evidence does not show that naive reference reduction fixes the problem. Identities with large usable reference sets perform substantially better in the holdout evaluation.

The original report printed 2,123 `duplicateReferenceContentGroups`, 1,729 `crossLabelDuplicateReferenceGroups`, and 755 assigned/Unknown mixed target groups. These values are **not accepted as contamination evidence** because a single group photo naturally contributes multiple face rows with one `contentGroup`.

The corrected source-content audit found:

- **416** reference content groups spanning multiple photo revisions, covering **833** photo revisions and **949** reference faces.
- **473** same-identity content groups repeated across photo revisions; all **949** repeated-content reference faces were represented in these same-identity groups.
- **0** cross-label near-duplicate face-pair candidates at cosine `>=0.95`.
- Maximum cross-label similarity across repeated content was only **0.4317**.
- **419** target content groups spanned multiple photo revisions, covering **983** target faces.
- No current identity had merge-history evidence in this sample.

The corrected audit therefore found repeated source copies but no cross-label near-duplicate evidence suggesting confirmed identity contamination. Multiple faces in one group photo are explicitly excluded from this conclusion.

One additional report-display quirk is documented: the scenario-level `knownWithoutUsableReference` count of **37** is authoritative. The segmentation's `no-usable-holdout-reference` row displays zero because targets skipped before ranking have no record for the segment summarizer.

### Evidence-based interpretation and maintainer decision

Current data does not justify raising/lowering the global score thresholds, replacing max-exemplar matching with a centroid, or applying an eight-reference quality/diversity cap. The High policy is already extremely precise and conservative. The strongest opportunities are instead around **quality-aware handling of weak/small faces** and **strengthening evidence for identities with sparse reference sets**.

On 2026-09-15 the maintainer selected the conservative direction:

1. **Keep the current max-exemplar ranking and current High score+margin policy.**
2. Do not globally cap rich identities or replace matching with the evaluated centroid/cap alternatives.
3. Treat quality-aware handling of weak/small faces and strengthening sparse identities through assisted review/anchors as possible future improvement slices rather than changing the accepted matcher in WI-0081.
4. Require any future production candidate to rerun the same private baseline and preserve High precision / Unknown High-emission guardrails.

WI-0081 closes as an investigation with existing production matching semantics unchanged.

## Investigation acceptance criteria

- [x] A repeatable local evaluation set is defined from reviewed catalogue evidence or an existing privacy-safe evaluation workflow.
- [x] Current top-1 and useful top-k suggestion accuracy are measured with sample size and queue-selection caveats recorded.
- [x] Accuracy is segmented by at least confidence, face quality/size, identity/reference-set size and model revision where data allows.
- [x] Reference-data contamination/duplication and identity-merge effects are audited. The corrected source-copy audit found repeated content but no cross-label near-duplicate candidates; merge-history evidence was zero in this sample.
- [x] Genuine/impostor score behavior is inspected sufficiently to determine whether calibration/ranking drift is involved.
- [x] At least two mitigation families are compared, including a non-model approach where plausible.
- [x] Proposed changes include a before/after evaluation plan and a guard against increased false positives.
- [x] The maintainer selects the implementation direction before production suggestion behavior changes.

## Source finding

During the 2026-08-26 maintainer verification, the maintainer separately reported that suggestion accuracy appears to be worsening. The planned M19/M20 functionality itself passed verification, so this is tracked as a new quality investigation rather than reopening those acceptance checks.
