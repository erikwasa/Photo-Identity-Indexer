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
