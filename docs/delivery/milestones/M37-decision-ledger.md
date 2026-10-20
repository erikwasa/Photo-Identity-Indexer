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

## Final manual-suggestion pilot acceptance and native product follow-up (2026-10-21)

After PR #631 merged, the maintainer successfully reviewed **129/129**
automatically suggested technical candidates in Archive cleanup:
**72 Keep, 37 Dismiss, 20 Consider exclusion**, zero automatic actions.
Technical reason counts overlap; low sharpness was the dominant suggestion
reason (96/129). **Most of the 20 photos considered for exclusion were
genuinely unwanted**, in the maintainer's judgment, and the process was
**reasonably convenient**. These are qualitative usefulness observations,
NOT objective false-positive precision, representativeness, recall or
deployment safety evidence.

**Manual-suggestion candidate generation and local human-review
feasibility: GO.** Advance a first native in-app operator-controlled
review workstream in issue #629, using existing local durable proxies,
bounded deterministic technical suggestions, durable decisions across
restarts, a Consider-exclusion follow-up filter and independent review
context switches. The native work uses the **existing separately
confirmed source-copy purge only after an explicit human action** and
never bulk-excludes. No collection/slideshow eligibility or producer
background jobs are modified. See
`docs/operations/native-quality-suggestion-review.md`.

**M37 adoption boundaries remain explicit:**
- Quality preference in actual saved collections: **DEFER** (WI-0193
  TOPIQ pairwise research-only, 0/80 coverage in WI-0195 live scenarios,
  no operator-approved new model weights/license/backfill).
- Very Low **automatic** exclusion: **NO-GO**.
- Visual redundancy **automatic** suppression: **NO-GO**.
- Combined automatic suppression: **NO-GO**.
- **Human-reviewed technical low-quality suggestions: GO for a
  manually controlled product experience**, not unattended quality
  exclusion and not an approval for all photo/smart/slideshow contexts.
- The original WI-0195 four-policy acceptance remained **unmet**.
  Close or defer the evaluation milestone only with this distinction
  recorded; native operator UI is a separate subsequent product
  workstream, not a retroactive completed four-arm experiment.

## Final M37 disposition — evaluation closed with research NO-GO / DEFER (2026-10-21)

**The goal of M37 was to evaluate whether independent quality and
visual-redundancy signals can be safely applied.** The empirical answer is
that automated exclusions/suppression cannot be approved with this
evidence, while **human-controlled technical-quality suggestions have
demonstrated practical value**. Closing a *research evaluation* with
negative/deferred policy decisions is not a claim that those policies
were implemented. The original WI-0195 four-arm policy simulation
acceptance was **not demonstrated and its rollout track is cancelled**.

| Evaluated capability | Final M37 decision | Evidence and boundary |
| --- | --- | --- |
| Representative quality review protocol (WI-0192) | **PASS** | 400 representative + 200 challenge benchmark; frozen calibration/heldout |
| Selective TOPIQ preference (WI-0193) | **RESEARCH SIGNAL / PRODUCTION DEFER** | Frozen p75 score gap 0.16755089163780212; heldout point estimate only, confidence/license and original/proxy transfer not sufficient for deployment |
| Absolute quality tier or unattended Very Low exclusion | **NO-GO** | Not safe enough to exclude meaningfully distinct photos without human review |
| Archive-scale visual redundancy candidate discovery (WI-0194) | **PASS AS RESEARCH** | 27,050 proxies; 200 calibration plus 180 guarded heldout blind pair reviews; not representative whole-archive precision |
| Unattended visual redundancy suppression | **NO-GO** | No heldout candidate-stratum 95% Wilson lower bound met conservative suppression precision; unobserved leakage and missing disagreement controls |
| Combined quality + redundancy automatic policy | **NO-GO** | Both prerequisites rejected / not deployment-approved |
| Original WI-0195 four-arm real-collection policy comparison | **NOT DEMONSTRATED / CANCELLED** | Safe preview importer, 8/8 shadow tests; 80 candidates, 0 pre-existing TOPIQ scores, 0 effective score comparisons; opt-in bounded scoring deliberately not authorized |
| WI-0195 bounded score-coverage preflight | **PASS PLANNING** | 6+8 synthetic tests passed, 80/80 existing current proxies, 80 prospective scored photos fitting cap 120, 353.8 seconds projected CPU, **zero inference** |
| Human-controlled low-quality suggestions (issue #629) | **GO FOR MANUAL PRODUCT EXPERIENCE** | 129/129 Archive suggestions human-reviewed; 72 Keep, 37 Dismiss, 20 Consider exclusion. Maintainer confirmed most considered cases genuinely unwanted, and review reasonably convenient; 0 automatic exclusions |

**Follow-up ownership:** WI-0629/issue #629 is a separate ongoing
product-development workstream. PR #633 merged the native
`/quality-suggestions` page with durable per-context review choices
and an operator-triggered bounded scan. It requires a **small local
native UX verification** before product acceptance; M37 research closure
does NOT pre-approve a native production experience, automatic
photo filtering, background inference or broad archive claims.
The previous companion's 129 private human choices are not silently
imported. Keep them confidential. Any future reversible hide,
whole-archive batch scanning, model weight/license approval or semantic
redundancy suggestions requires separate explicit product scope.

The original M37 work-item statuses are now:
WI-0192 **completed**, WI-0193 **completed**, WI-0194
**completed (candidate discovery/evaluation, suppression NO-GO)**,
WI-0195 **cancelled (four-arm acceptance unmet)**.
M37 itself is **completed as an evaluation with these outcomes**.

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
