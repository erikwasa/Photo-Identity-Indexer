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

## Substantive final bounded research phase (2026-10-20; local plan/permission pending)

The first local real Creative preview test used 80 Fideli candidates and
20 target slots, passing **all 8 simulator tests** with 0/80 overlap
against the prior 600-photo TOPIQ scores, 267 abstentions from missing
evidence, and zero changes. This is confirmed **score coverage failure**,
not evidence that research quality preferences harm or help actual
collections; no blind review of identical selections is appropriate.

This PR develops an **end-to-end bounded, privacy-preserving answer**:
- Read-only `collection-shadow candidate-proxies` .NET command binds a
  private captured scenario manifest and resolves at most **240**
  current `jpeg-1600-q78` durable proxy metadata entries, without
  source originals, catalogue writes or hydration. Missing/stale proxies
  fail closed; SHA-bound private output is exclusive-write.
- `collection_shadow_batch_score.py plan` validates scenario, map and
  existing TOPIQ version/evidence **without importing a learned model
  or opening proxy pixels**. It prints only aggregate overlap, at most
  **120** additional photo candidates for an optional research run,
  approximate prior CPU cost, and license/identity warnings. Safe
  read-only plan is the next operator action after PR merges.
- The separate **explicit opt-in `score` mode** requires research-only
  third-party weight consent, hashes/size-verifies **all** current
  proxy bytes *before* model initialization, enforces installed
  `pyiqa 0.1.16` and `topiq_nr`, caps new inference to 120 photos,
  copies previously scored TOPIQ values only where versions match,
  and writes new private source-/proxy-/score-bound score exports
  distinct from WI-0193's consumed benchmark. Third-party model
  weights may be downloaded; this is **not** proof of checkpoint
  licensing or deployment permission.
- Reuse the existing strict original-kind and existing-selection-score
  contiguous-tier score-gap `0.16755089163780212` selection
  simulator and bound private blinded reviewer with Finish & Report.
  No rank retuning; no absolute tier, Very Low exclusion, redundancy
  or combined automatic suppression. Run only if evidence actually
  changes the same-sized collection selection.
- Allow multiple *distinct* read-only preview imports into one
  scenario manifest and isolated per-batch private artifacts, with
  synthetic tests for provenance/consent, mismatched manifests,
  tampered local proxies, bounded inference and no-overwrite behavior.
  One Windows PowerShell operator runner performs tests, plan, optional
  separately authorized scoring and optional A/B review. No large
  photo-archive rerun and no repeated per-command development PRs.

**Closure gate:** After one bounded result (or an explicit defer if
third-party inference is not authorized), record the final four-policy
GO/NO-GO/DEFER evidence decisions and close M37 as a completed **evaluation**
only if the original unmet four-arm criteria are transparently
documented as not demonstrated. WI-0195 is not production acceptance;
the full old four-policy checklist remains unchecked. Any production
score backfill, weight/license attestation, original/proxy transfer,
absolute classification or suppressor policy belongs in new future
work outside this milestone. Runbook:
`docs/operations/collection-shadow-simulation.md`.

## Operator-controlled manual suggestion direction (2026-10-20)

**The maintainer's desired outcome is an automatically generated shortlist of
photos that may be low quality, with the final decision to keep, dismiss or
exclude always made manually.** Suggestion precision need not satisfy the
98%+ safety bar for *unattended* Very Low exclusion. The same applies to
potential visual redundancy: semantic/near-copy candidates may be displayed
for review without automatically suppressing meaningful photos.

The current WI-0195 offline shadow simulator answers a narrower *quality
preference among otherwise equivalent collection candidates* question.
It does not yet provide a Photo Identity-wide low-quality suggestion queue.
Do not treat that as failure of manual suggestion usefulness or infer that
any approved production review UI exists. Explicitly evaluate use cases
**separately and under the operator's control**: Archive-wide suggestion
review, per-collection/creative selection, slideshow curation, and
global Photo Identity exclusion. The operator may opt in/out for each use
case; no single threshold should silently change them all.

**Critical safety difference:** Existing source-copy 'Exclude from Photo
Identity' action permanently purges local retained Photo Identity data
while leaving original OneDrive bytes untouched (ADR-0008, WI-0091).
A suggested candidate must never trigger that action on its own.
Manual keep/dismiss/skip and an explicitly confirmed exclusion are
distinct steps. A reversible per-use-case hide needs a separate
implementation, not the destructive exclusion API.

Carry the substantial implementation into
[operator-controlled low-quality suggestions #629](https://github.com/erikwasa/Photo-Identity-Indexer/issues/629);
M37 should record a separate manual suggestion feasibility decision based on
accepted/rejected candidates, false-alarm burden and usefulness in real
scenarios. Automatic Very Low exclusion and automatic redundancy suppression
remain NO-GO on the original research evidence. Preserve the originally unmet
four-variant WI-0195 acceptance instead of claiming it passed.

## Final bounded WI-0195 verification and user-directed next step (2026-10-20)

Maintainer ran the updated read-only default path successfully:
**6/6** new bounded scoring tests and **8/8** prior shadow tests PASS,
read-only durable proxy mapping **80/80**, **0/80** previously scored
TOPIQ candidates, **80** prospective new scores under the hard 120
cap, estimated ~354 seconds, **no inference**, no originals,
hydration, persistence or production model/ranking approval. This
closes the bounded **planning** acceptance; it does *not* establish
real collection preference or authorize optional model weights.
Do not run `-Score` without an explicit separate operator
rights/utility decision.

The operator's clarified priority is now *automatic suggestions
for manually reviewing/excluding poor photos*, tolerating some
false positives, with operator-selectable use cases. WI-0195's
original attempted four automatic policy arms remain not accepted.
First usable, non-destructive technical-suggestion workbench is
tracked in [issue #629](https://github.com/erikwasa/Photo-Identity-Indexer/issues/629)
and `docs/operations/manual-quality-suggestions.md`, with
follow-up native app integration contingent on maintainer review
utility. M37 should document **manual-suggestion feasibility**
separately from its existing automatic-exclusion NO-GO, and
record the originally unachieved WI-0195 arms transparently.

## Verification requirements

Automated simulator determinism tests plus blinded maintainer review of representative real collection outputs and privacy-safe aggregate comparison evidence.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:

## Final work-item disposition — 2026-10-21

**WI-0195 research disposition COMPLETED; original four-arm automatic-policy acceptance NOT DEMONSTRATED**, following independent quality and visual
redundancy policy no-go decisions. This is not a claim that
baseline vs approved Very Low, redundancy-only and combined
four-arm selection was evaluated successfully.

Completed non-destructive portions: real Creative preview
import; 8/8 shadow tests on maintained data with
80 selected candidates, **0/80 existing TOPIQ scores**
and no score comparisons/selection changes; 6/6 additional
bounded-scorer tests, 80/80 verified current review
proxies, plan to score at most 80 new photos with
explicit rights/consent, **no model inference performed**,
no originals read/hydrated, no catalogue/collection changes.
Original four approved policy variants remain unavailable:
selective TOPIQ preference only research evidence (production DEFER),
absolute Very Low exclusion NO-GO, automatic redundancy
suppression NO-GO, combined policy NO-GO.

**Separate useful product outcome:** 129/129 manually reviewed
technical candidates under issue #629, with 20 marked
Consider exclusion and maintainer finding most genuinely
unwanted. Human-controlled suggestions GO for product
development, not any unattended automatic exclusion.
Final M37 acceptance documented in
`docs/delivery/milestones/M37-decision-ledger.md`.
