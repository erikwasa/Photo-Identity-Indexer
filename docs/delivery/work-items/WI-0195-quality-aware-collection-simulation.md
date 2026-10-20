---
id: WI-0195
title: Simulate quality-aware visual redundancy during collection creation
milestone: M37
status_source: PhotoIdentity.Docs
depends_on: [WI-0193, WI-0194]
related_adrs: []
affected_modules: [PhotoIdentity.Core, tools, docs]
---

# WI-0195: Simulate quality-aware visual redundancy during collection creation

## Objective

Test whether independently evaluated absolute quality and visual redundancy actually improve real collection creation before Photo Identity persists either signal or changes production selection behavior.

## In scope

- Reuse only quality and redundancy policies accepted by WI-0193/WI-0194; do not retrain either signal using collection-level preferences.
- Run representative maintained-archive collection scenarios with four variants: current behavior, quality preference only, visual redundancy suppression only, and combined quality + redundancy.
- Include Smart-, Creative- and search-anchored candidate pools where those paths are available, with varied small/large pools and content types.
- Keep collection eligibility/relevance semantics unchanged in the simulator. Quality is a bounded preference after eligibility/relevance, not permission for unrelated high-quality photos to outrank relevant candidates.
- Simulate Very Low exclusion separately so its value/risk can be reviewed independently from quality preference.
- When candidates are judged redundant by the accepted similarity policy, prefer the independently calculated absolute quality rating as the primary representative selector, with deterministic fallback to existing collection score/order when quality is tied/unavailable.
- Prefer greedy collection-time suppression against already-kept representatives rather than creating canonical permanent similarity groups unless evaluation evidence specifically justifies grouping.
- Produce blinded comparison output so the maintainer can choose/prefer collection variants without knowing which policy generated them.
- Measure collection preference, repetitive-photo count, obviously poor-photo count, meaningful-photo loss, target-size attainment and how often the higher-quality candidate wins within redundant sets.
- Finish with explicit separate go/no-go decisions for quality preference, Very Low exclusion, redundancy suppression and the combined policy, plus a bounded production integration plan.

## Out of scope

- Persisting production quality scores or visual embeddings.
- Changing Search/Smart/Creative APIs/UI or saved collection semantics.
- Automatically deleting/hiding photos.
- Creating final production work inside this item.

## Acceptance criteria

- [ ] Representative real collection scenarios produce all four policy variants without mutating saved collections or slideshow snapshots.
- [ ] Blind maintainer review can compare variant quality and redundancy without model/policy labels leaking into the choice.
- [ ] Combined behavior reduces unwanted repetition and/or poor-photo selection without unacceptable loss of distinct meaningful photos or failure to reach requested collection size.
- [ ] Redundant-set representative choice uses absolute quality independently of the similarity model and records deterministic fallback behavior.
- [ ] Quality preference never overrides eligibility and remains bounded relative to search/collection relevance.
- [ ] Separate adoption decisions are recorded for quality preference, Very Low exclusion, visual redundancy and combined collection use.
- [ ] If production adoption is justified, the completion notes define the smallest follow-up persistence/API/selection/UI work rather than implementing it here.

## Constrained research implementation: non-destructive shadow comparison (2026-10-20)

**Dependency and policy decision:** WI-0193's frozen TOPIQ NR p75 raw-score-gap rule (`0.16755089163780212`) passed a one-shot held-out **research-only selective ordering** point estimate, not an absolute per-photo quality tier, production ranking, model-license decision or Very Low exclusion. WI-0194 independently finished **200 calibration + 180 guarded held-out pair reviews** but issued an explicit **NO-GO** for automatic visual redundancy suppression: candidate strata are selected and photo-correlated, none of their held-out 95% descriptive Wilson lower bounds crosses a safe 95% production target, and the dHash-far/CLIP-high disagreement bucket had no guarded held-out cases. The original four-arm WI-0195 acceptance remains blocked because neither production-quality policy is approved.

**This bounded development branch is NOT four-arm acceptance.** Build the parts that can be tested without changing the existing product or manufacturing policy approvals:

1. `tools/PhotoIdentity.PhotoQualityEvaluation/collection_shadow_simulator.py` imports existing read-only Creative Collection preview `selectedCandidates` (Smart or search anchor), or a strict private Smart/search scenario capture with explicit original rank, candidate kind, existing integer relevance score and a lower simulated target count. Only the **existing shortlisted eligible** photos are considered: no queries, default selection, saved collection or slideshow snapshot are mutated. The shadow can only move a photo within a contiguous run of identical existing `selectionScore` and `kind`, using the exact frozen TOPIQ 75th-percentile raw-score difference; missing score evidence or smaller gaps abstains. The original `private/topiq_nr-scores-600.json` has **sparse 600-photo archive coverage**, so a no-effect result is expected on many real scenarios. No new model inference, labels, archive-wide production backfill or relative percentile quality conversion is allowed.
2. Produce separate private exclusive-write **blinded A/B shortlist** and research audit, bind by digest and record candidate/selected score coverage and differences. Show disabled rather than implied success for three arms: Very Low automatic exclusion, redundancy-only suppression, and combined quality+redundancy suppression.
3. Optional read-only .NET proxy metadata resolver maps only captured shortlist revisions, with source-root path safety checks and immutable SHA; loopback-only blinded A/B local reviewer displays review proxies, keyboard choices, private autosave/resume, and a **Finish & Report** action. Bounded per-source/simulation input size and private-only files. A score-coverage/unchanged-selection gate stops before prompting reviews of identical lists. Aggregate preference report never exposes IDs, scores, photo paths or original model identity.
4. Synthetic determinism, blinding, malformed-score/schema, frozen-gap, relevance/eligibility nonpromotion, disabled-arm, SHA/file-integrity and private no-overwrite tests. **One bundled Windows operator verification round** in `docs/operations/collection-shadow-simulation.md`.

**No-go / acceptance rule:** This implementation is an **exploratory research infrastructure slice**, not formal WI-0195 acceptance; it does not deliver the original four validated variants, collection representative preference, Very Low exclusion, redundant-set selection, or a bounded production integration plan. The existing full acceptance checkboxes must remain unchecked. Do not change dependency status to completed or product activation. On aggregate local evidence, decide whether an independent, properly licensed and adequately scored research experiment merits a NEW explicitly scoped follow-up. If the existing 600-score coverage is inadequate, report that as a blocker and defer rather than spuriously simulating a deployed quality model.

## Verification requirements

Automated simulator determinism tests plus blinded maintainer review of representative real collection outputs and privacy-safe aggregate comparison evidence.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
