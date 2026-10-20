# M37 — final decision ledger and closure route

**Status: evaluation milestone in progress; no production quality or redundancy behavior enabled.**
Updated 2026-10-20. This is the decision record for M37, **not** a
retroactive claim that all originally drafted experiments passed.

## Why the milestone exists

Determine, before any production selection change, whether independently
measured per-photo quality and archive-wide visual redundancy can safely
improve photo collections. A scientifically honest **NO-GO** is a valid
evaluation outcome; it does not license the application to exclude photos.
The default choice is to protect existing meaningful photos and preserve all
current Search, Smart, Creative and slideshow behavior.

## Evidence and decisions

| Goal | Evidence already obtained | Outcome |
| --- | --- | --- |
| Representative benchmark and blinded tiers — WI-0192 | 400 representative photos plus 200 challenge, frozen 240/160 representative calibration/held-out groups and reproducible blinded reviews | Complete / accepted |
| Compare technical baseline and modern IQA — WI-0193 | WI-0182, TOPIQ NR, MUSIQ, ARNIQA, 600 private review-proxy scores; one frozen TOPIQ 75th-percentile gap `0.16755089163780212` tested once | Complete / research-only ranking signal |
| Absolute photo-quality tier | Three-band and tier classification did not reliably outperform baseline | **NO-GO** |
| Automatic Very Low exclusion | 3 known Very Low in calibration; inadequate high-precision evidence | **NO-GO** |
| Quality-model deployment | Third-party checkpoint identity/license, original-versus-proxy transfer and full-archive runtime/backfill not accepted | **NO-GO** pending separate work |
| Archive-wide visual candidate discovery — WI-0194 | 27,050 proxies; 365.8 million bounded exact dHash comparisons; 165,181 enriched candidate/control pairs; 200 calibration + 180 guarded held-out human pair reviews | Feasible as research candidate discovery |
| Automatic similarity suppression | Selected/correlated held-out groups below safe Wilson lower bound; no held-out far-dHash/CLIP-high dispute controls; broader transitive grouping creates giant families | **NO-GO** |
| Automatic quality+redundancy combined policy | Requires TWO separately approved policies; neither approved | **NO-GO** |
| Real collection experience — WI-0195 | Offline preview simulator, 8/8 tests and safe local Creative scenario: 80 candidates, 0 prior TOPIQ scores, 0 changed selections | Tooling accepted; useful human comparison **not yet measured** |
| Bounded score-covered collection-shadow experiment | New read-only candidate proxy map (<=240), no-inference plan, separately authorized <=120 new private research scores per batch, multi-scenario import, existing blinded finish/report viewer | Development in current bundled PR; pending one local feasibility/review round |

## New operator distinction: manual review is not automatic exclusion (2026-10-20)

The maintainer clarified that the **primary user-facing goal is automatically
suggesting possibly low-quality/unwanted photos for the operator to review**,
not silently removing them. It is acceptable for suggestions to include some
false positives when the operator has a practical, safe **keep/dismiss/skip**
decision and makes every exclusion decision personally. This changes the
evaluation/UX question substantially; it **does NOT overturn** the M37
NO-GO conclusions for unattended Very Low exclusion, automatic redundancy
suppression or combined hidden-photo policies.

Record the separate follow-up in
[manual low-quality suggestion issue #629](https://github.com/erikwasa/Photo-Identity-Indexer/issues/629).
The low ~98%+ high-precision bar applied to *automatic destructive
exclusion*, **not** to ranked manual suggestions. A useful suggestion
pipeline should instead report **discovery yield, false-alarm/review burden,
human acceptance rate, saved review time and meaningful-photo preservation**,
evaluated by the operator on real use cases. A weak quality score may surface
a good/sentimental image, and a high score may miss one they dislike; the
system must make uncertainty understandable.

The operator explicitly decides **which use cases are worthwhile**:
standalone Archive cleanup suggestions, use-case-specific collection/slideshow
candidate warnings, or Photo Identity-wide exclusion. No single score
automatically changes every context. Existing Photo Identity source-copy
**Exclude & purge** from ADR-0008/WI-0091 is a **durable retained-data purge**,
not a reversible hide; source originals remain in OneDrive, but catalogue
metadata/proxies/identity evidence are removed and re-inclusion requires
reanalysis. Suggestions must never invoke it without a separate explicit
operator confirmation. A reversible "hide from this collection/slideshow"
would require a distinct product design, not reuse this purge endpoint.

**Updated M37 completion interpretation:** Finish the bounded, read-only
score-coverage experiment as planned, then ask whether **manual suggestion
review** is useful enough to warrant issue #629's product development.
Document this as a separate **GO/NO-GO/DEFER** research decision.
Full operator-facing exclusion integration and per-context settings remain
follow-up work; do not claim they exist just because M37 scoring tools do.
The original four-arm WI-0195 automatic-policy acceptance remains unmet.

## Bounded WI-0195 real-archive preflight passed; prioritize manual suggestion value (2026-10-20)

The maintainer ran the corrected `run_bounded_collection_shadow.ps1`
against the existing 80-photo Creative shortlist:
**6/6 bounded-score tests PASS + 8/8 shadow tests PASS**; all 80
current durable review proxies matched, **0 pre-existing TOPIQ scores**,
80 new score candidates <=120 hard cap, estimated research CPU
353.8 seconds, and a **strict no-inference** plan. No source
originals/hydration, catalogue writes or production changes.
The user now explicitly prefers manually choosing what to exclude
from an **automatic suggestion queue** rather than another isolated
collection-ranking benchmark. **Do not interpret a passed planning
gate as approval to download third-party TOPIQ weights or perform
inference.** Record bounded WI-0195 plan acceptance PASS, quality
collection effect still UNMEASURED/DEFERRED pending separate
model-licensing and usefulness decisions.

Issue #629 now begins a **first substantive usable local review phase**:
`manual_quality_suggestions.py` consumes existing bounded read-only
`proxy-technical-quality-v1` evidence; the local review workbench
supports human-controlled Keep, Dismiss, Consider exclusion, per-use-case
**review** switches and private resumable state. It cannot trigger native
Exclude & purge or change production collection/slideshow eligibility.
See `docs/operations/manual-quality-suggestions.md`. Review utility,
not unattainable unattended-exclusion precision, is the new *separate*
research question. Do not mark full M37 four-arm acceptance as met.

## What must happen NEXT before a closure recommendation

1. Merge one substantive bounded-scoring + milestone-documentation PR after
   CI passes. On the maintained archive, run **one no-inference local
   preparation and coverage plan** against the already captured Creative
   scenario, optionally a few more independent previews. Do not rescore
   the entire archive.
2. Operator explicitly decides whether to authorize one bounded CPU TOPIQ
   research-only inference on **existing hash-verified review proxies**.
   The installed `pyiqa 0.1.16` model may load or download third-party
   weights. This **does not confer a production license or model identity
   attestation**. If authorization is withheld, record a research-scope
   no-go/defer and proceed to disposition; do not demand repeated commands.
3. If authorized and score coverage materially increases, run the frozen
   same-kind/same-relevance-gap shadow selection on captured REAL
   Creative/search-anchored previews. Only if one or more resulting
   collections change, ask for ONE blinded human A/B review round using the
   already-built Finish & Report UI. Interpret wins/losses, retention and
   score coverage honestly; do not use these preferences to retune TOPIQ
   cutoff or select another IQA model.
4. Record **one explicit end-of-M37 adoption memo** for four independent
   decisions: selective quality preference; Very Low exclusion; automatic
   visual redundancy; combined policy. Each must be `GO`, `NO-GO` or
   `DEFER` with a clear reason. Quality preference may remain research-
   promising without becoming a production GO.
5. Close the evaluation milestone **with documented no-go/defer outcomes**
   when the decisions and bounded collection experience experiment are
   assessed. Retain the original unmet four-variant/suppression criterion
   in work-item history; label it **not demonstrated due to research no-go**
   rather than claiming all four deployed policy arms were validated.
   Any future model license/weight pinning, absolute quality backfill,
   ANN/similarity-model work, production persistence or collection UI
   adoption belongs in **new work outside M37**, not an endless sequence
   of M37 micro-checkpoints.

## Safety and privacy evidence that cannot be relaxed

- Private per-photo IDs, reviews, model scores and proxy paths remain in
  `private/` and never go to GitHub or the assistant. Share aggregate-only.
- Never reopen WI-0193's consumed quality held-out labels or retune the frozen
  score gap; do not reuse WI-0194 reviewed held-out pair labels for tuning.
- No production original hydration, photo analysis, catalogue mutation, Smart/
  Creative/Search eligibility change, automatic exclusion or persistence.
- Sparse existing 600-score overlap is a **coverage blocker**, not evidence
  that selective ranking cannot help users. If local third-party inference
  is unauthorized, the milestone can still conclude **DEFER**.
