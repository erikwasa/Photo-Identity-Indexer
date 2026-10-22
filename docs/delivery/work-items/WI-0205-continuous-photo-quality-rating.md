---
id: WI-0205
title: Evaluate and productize a provenance-bearing continuous photo-quality rating
milestone: M40
status_source: PhotoIdentity.Docs
depends_on: [WI-0193]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Core.Tests, PhotoIdentity.Integration.Tests, docs]
---

# WI-0205: Evaluate and productize a provenance-bearing continuous photo-quality rating

## Objective

Give one immutable photo revision an optional, interpretable **continuous quality rating** usable for human-assisted choice between similar eligible photos. It may later support explicitly enabled collection/search ordering, but must not silently change existing membership, relevance, slideshow output or source-copy identity.

## Origin and status

Migrated from the **open, issue-only quality-rating proposal #639 (2026-10-21)** during the 2026-10-22 GitHub issue retirement audit. There was no canonical delivery work item for its product requirements before this migration. This file and the M40 canonical status registry supersede issue-based planning. **No model or production rating has been approved, implemented or verified by this administrative migration.**

## Why

The maintainer wants a meaningful fine-grained numeric quality signal, **not merely low/medium/high tiers** or an arbitrary 0–100 relabel of raw model output. A technical blur/sharpness suggestion does not by itself measure composition, aesthetic judgment, significance or the image's value to the owner.

M37/WI-0193 obtained **research-only** selective pairwise TOPIQ p75 evidence, with limited statistical confidence and unresolved model/proxy rights and transfer risks. Absolute quality bands/Very Low exclusion were **NO-GO**, production deployment **DEFER**, and the one-time held-out labels are consumed and **must not be retuned or re-evaluated**. WI-0194 automatic visual suppression is also NO-GO. A new, independently justified data/validation plan is required.

## In scope

- Define score semantics and documented limitations: technical defects versus subjective aesthetics versus sentimental value; monotone direction, comparability, uncertainty, intended domain and when to abstain.
- Evaluate candidate continuous/pairwise mapping against an **independent** appropriate representative sample (no reuse of the consumed WI-0193 held-out ratings). Explicitly test portrait subject/blurred background bias, device and exposure changes, old scans, and input-resize/proxy versus original-pixel transfer.
- Only offer an arbitrary-looking 0–100 display after evidence supports calibrated stable meaning. No percentile/range normalization that changes a photo's nominal rating when archive contents change unless explicitly defined and accepted as an archive-relative feature.
- Resolve independent framework/model-weight identity, license, deployment rights, CPU/GPU runtime and storage, input-resolution policy and one-time scoring economics before any inference-enabled product rollout.
- Store optional revision-bound versioned rating with exact model/version/proxy profile/source evidence, analysis timestamp, coverage/missing-data handling, score direction and calibration/uncertainty provenance.
- Any inference/backfill is explicit opt-in, bounded, cancellable/resumable and operator-controlled; no surprise whole-archive load, downloaded weights or unexpected OneDrive hydration.
- Start with **within the same collection eligibility/relevance tier** quality comparisons of visually similar candidate photos, with clearly distinguishable missing-score fallback and no changes to other collections.
- Define independent per-use-case opt-in for any later sorting/ranking in Search, Smart/Creative Collections or manual cleanup; regression tests must preserve existing defaults.

## Out of scope

- Automatically excluding/purging sources or suppressing whole visual review sets.
- Reusing M37's consumed held-out human labels to select another model or retune thresholds.
- Claiming that the current dHash distances, technical scan priority or TOPIQ score are calibrated universal photo-quality scores.
- Deploying third-party weights, archive-wide quality backfill or altering collection/search behavior merely by accepting this plan.

## Acceptance criteria

- [ ] Operator agrees on an interpretable continuous rating contract, provenance, coverage and abstention UX, independent of emotional significance.
- [ ] Model/evidence/legal and proxy/original limits documented, including uncertainty and domain biases on an independent representative validation cohort.
- [ ] Score units and any displayed range are calibrated and stable; false precision avoided.
- [ ] Inference/background persistence and exact revision/model/proxy provenance are explicitly opted into with cancellation, resume and no original hydration.
- [ ] Initial same-relevance/similar-photo use case demonstrates measured operator utility versus unchanged baseline; missing scores cause safe fallback.
- [ ] Existing Archive, Search, Smart, Creative, snapshots and source-copy/privacy semantics remain unchanged unless independently approved for each use case.
- [ ] No automatic purge, unattended suppression or consumed-held-out retuning is introduced.

## Verification requirements

Independent, human-inspected calibration/validation design plus Core/API/database tests for versioned scores, bounded execution, missing-score fallbacks, opt-in controls and no effect when disabled. Do not commit private photos, ratings, score arrays, model weights, photo IDs or credentials.

## Completion notes

- State: **proposed**, not implemented.
- Research dependencies: [WI-0193](WI-0193-absolute-photo-quality-model-evaluation.md), [WI-0194](WI-0194-archive-wide-visual-redundancy-evaluation.md), [M37 decision ledger](../milestones/M37-decision-ledger.md).
- Product neighbors: [WI-0203](WI-0203-creative-collection-similarity-review.md) manual visual review, [WI-0204](WI-0204-reversible-collection-scoped-alternative-omission.md) optional reversible recipe-scoped omission, [WI-0206](WI-0206-native-quality-suggestion-review.md) completed technical Archive suggestions. None authorizes a continuous rating by implication.
