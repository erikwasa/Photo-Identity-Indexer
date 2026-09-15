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

The symptom is currently qualitative rather than a quantified regression. Before changing thresholds, ranking, representative embeddings or models, Photo Identity needs evidence that identifies whether the degradation comes from data quality, reference-set growth, incorrect/weak identity evidence, model behavior, ranking policy, face quality, score calibration, or another source.

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

The first implementation slice extends the existing privacy-safe reviewed-sample workflow rather than adding a second source of identity truth. `PhotoIdentity.ClusterEvaluation` exports one bounded exact-model reviewed sample with pseudonymized Person/photo/content groups. For WI-0081 it also records the exact persisted suggestion policy, detector confidence, normalized face area, review time and whether an assignment originally referenced a subsequently merged Person. Person names, source paths, crop paths, raw catalogue identifiers and policy actor data are not exported.

`tools/cluster-evaluation/evaluate_suggestions.py` evaluates the sample locally without writing production suggestions or assignments. It reproduces the current best-exemplar-per-Person ranking as a production-equivalent reference, then uses a duplicate-resistant leave-one-out baseline that excludes every reference from the target's exact-content group. Reviewed identities that have no remaining same-Person holdout reference are reported separately rather than counted as matching failures.

The report covers top-1/top-3/top-5 accuracy, current High/Medium policy behavior, conservative suggestion emission on reviewed Unknown faces, genuine-vs-best-impostor score distributions and overlap, exact-content/merge contamination, reference concentration, and segmentation by detector confidence, normalized face area, true-Person reference-set size and review chronology. Model revisions are evaluated separately by exact model hash rather than pooling incompatible evidence.

Two non-model reference/ranking mitigation families are compared offline against the duplicate-resistant current ranking: a normalized per-Person centroid and a bounded quality/diversity-selected reference set. Before/after deltas include top-1 accuracy, High precision/coverage and reviewed-Unknown High emission so an apparent accuracy gain cannot silently trade for greater false-positive risk.

This slice intentionally does **not** change production thresholds, reference construction, ranking, auto-assignment or review semantics. WI-0081 remains incomplete until the private catalogue evaluation is run, aggregate findings are recorded, and the maintainer selects an implementation direction.

## Investigation acceptance criteria

- [ ] A repeatable local evaluation set is defined from reviewed catalogue evidence or an existing privacy-safe evaluation workflow.
- [ ] Current top-1 and useful top-k suggestion accuracy are measured with sample size and queue-selection caveats recorded.
- [ ] Accuracy is segmented by at least confidence, face quality/size, identity/reference-set size and model revision where data allows.
- [ ] Reference-data contamination/duplication and identity-merge effects are audited.
- [ ] Genuine/impostor score behavior is inspected sufficiently to determine whether calibration/ranking drift is involved.
- [ ] At least two mitigation families are compared, including a non-model approach where plausible.
- [ ] Proposed changes include a before/after evaluation plan and a guard against increased false positives.
- [ ] The maintainer selects the implementation direction before production suggestion behavior changes.

## Source finding

During the 2026-08-26 maintainer verification, the maintainer separately reported that suggestion accuracy appears to be worsening. The planned M19/M20 functionality itself passed verification, so this is tracked as a new quality investigation rather than reopening those acceptance checks.
