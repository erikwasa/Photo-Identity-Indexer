---
id: WI-0194
title: Evaluate archive-wide visual redundancy embeddings and suppression thresholds
milestone: M37
status_source: PhotoIdentity.Docs
depends_on: [WI-0192]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Recognition.Onnx, PhotoIdentity.Persistence.Postgres, tools, docs]
---

# WI-0194: Evaluate archive-wide visual redundancy embeddings and suppression thresholds

## Objective

Determine whether Photo Identity can identify photos that look redundant enough that a generated collection normally needs only one representative, regardless of capture date, people, folder, event or location.

## In scope

- Compare existing dHash and CLIP evidence with dedicated visual/copy-similarity candidates such as DINOv2-style features and SSCD, subject to model licensing/deployment review.
- Keep exact duplicate identity, near-copy evidence, visual redundancy and semantic similarity explicitly separate.
- Retrieve nearest visual neighbours across the maintained archive without requiring same moment/date/person/folder/location.
- Build a blinded pair/group review set containing high similarity, medium/borderline similarity, candidate-model disagreements and random negative controls.
- Label review cases `Redundant`, `Similar but keep both`, `Not similar` or `Unsure`, where `Redundant` means the maintainer would normally want only one representative in the same generated collection.
- Tune candidate thresholds on calibration evidence and evaluate held-out redundancy precision/recall separately.
- Prioritize precision over recall; use approximately 95%+ observed precision as the minimum evidence for recommending automatic collection-time suppression, while reporting uncertainty and false-positive examples.
- Measure embedding generation runtime/model footprint, raw storage projection and archive-scale nearest-neighbour query cost.
- Measure exact-search practicality first and introduce no ANN/vector-index dependency unless maintained-archive measurements justify it.

## Out of scope

- Permanent production visual-embedding persistence.
- Changing current dHash Creative behavior.
- Deleting/merging source photos.
- Semantic search changes.
- Production collection suppression.

## Acceptance criteria

- [ ] Candidate methods are compared on archive-wide neighbours without date/person/location restrictions.
- [ ] Human review explicitly distinguishes redundant photos from semantically related or merely similar photos worth keeping together.
- [ ] A frozen threshold/model choice is evaluated on held-out reviewed cases and false-positive suppression examples are reported.
- [ ] Automatic suppression is recommended only with conservative precision evidence; otherwise the result records no-go/defer or manual-only use.
- [ ] Runtime, model size, vector storage and current-archive exact-query cost are documented before any ANN proposal.
- [ ] The result identifies whether one model is sufficient or whether separate near-copy and broader-redundancy evidence is justified.
- [ ] No production collection/search semantics change in this item.

## Checkpoint 1 — offline blinded pair-set foundation (2026-10-18)

Prepare an **offline, local private** deterministic review-pair sampler that
accepts a photo inventory plus existing candidate similarities supplied
later by archive-scale dHash/CLIP extraction. It must not pretend to be
a nearest-neighbour retriever itself or measure model performance.

- Distinguish exact content-copy controls, near-copy dHash candidates,
  high/medium/borderline similarity, dHash/CLIP disagreements, and
  explicit random-negative controls. Scores are **candidate sampling
  heuristics**, not human redundancy labels, model selection thresholds,
  or production suppressions.
- Keep same-content revisions, supplied near-copy family keys, and linked
  very-low-dHash candidates in the same deterministic **photo-disjoint**
  calibration/held-out partition. Cross-partition pairs are never
  evaluated, and the public aggregate stdout never prints photo
  identifiers, labels, paths or similarity values.
- Create two new private exclusive-write JSON outputs: a **blind
  review list** with only pseudonymous pair IDs and the two local revision
  keys needed by a future reviewer; and a separate **private audit** with
  partition, candidate evidence and sampling buckets for later protocol
  review. The blind list must not reveal buckets, scores, group,
  calibration/held-out status or model origins.
- Reject any supplied human pair labels, invalid identifiers, duplicate
  reversed pairs or malformed/nonfinite distances; never read the private
  quality study's consumed held-out human reviews.
- No actual human review, new model downloads/inference, model
  cutoff calibration, suppression, writes to the app catalogue or
  production collection changes in this checkpoint.

Next after merge: create private candidate-neighbour exports from the
maintained archive with bounded runtime/storage measurements and
real model/version/license provenance. Review the aggregate sampler
coverage before any blind human pair-label collection. No review-pair
held-out access is authorized yet.

## Checkpoint 2 — existing proxy/dHash/CLIP evidence export (2026-10-19; local acceptance pending)

Add an opt-in read-only CLI to generate real candidate neighbours from a **bounded** current-catalogue photo sample:
- Reuse only existing durable local review proxies, validating hash and length before computing the established OpenCV 64-bit dHash; do not load originals or trigger OneDrive hydration.
- Optionally read exact-fingerprint existing semantic CLIP embeddings in a PostgreSQL **READ ONLY** transaction; no new inference or schema initialization. CLIP remains semantic evidence, not an accepted visual-redundancy model.
- Deterministically select up to 5,000 current proxy-backed revisions (default 500); all-pair dHash scanning and bounded CLIP-seed-to-all-sampled-vector exact retrieval have no date, location, folder, people or moment restrictions.
- Include all within-subset dHash<=4 leakage links, nearest neighbours from both candidate signals and distinct screened random-negative controls. Never claim these recover near copies outside the sampled corpus.
- Exclusively create sampler-compatible private candidate JSON plus an aggregate local measurements report below private/. Log only aggregate counts/times; never commit identities, paths, labels, vectors or private results.
- Measure real population/sample coverage, CLIP indexing coverage, measured comparisons, verified proxy bytes, candidate count and wall clock time. Model licensing, inference runtime/footprint, full archive neighbour recall and threshold precision remain open.
- Add synthetic helper tests; separately request maintainer's aggregate local report before freezing a redundancy pair cohort.

See docs/operations/visual-redundancy-research.md for commands and the no-review/no-production guardrails. WI-0194 stays in progress; WI-0195 stays proposed. A green CI cannot establish maintained-archive retrieval acceptance.


## Checkpoint 3 — guarded full-proxy-population exact retrieval (2026-10-19; local acceptance passed)

The maintainer's read-only archive probes succeeded on deterministic current-catalogue samples:
- **500 dHash-only:** 27,050 proxy-backed current photos, 500 sampled, 124,750 exact dHash comparisons, 2,664 retrieved pairs, 100 filtered negatives; 47.3782 ms exact scan; 4,423.0172 ms proxy verification/hashing.
- **500 dHash + exact-fingerprint pinned CLIP:** 500/500 CLIP coverage, 49,900 seed comparisons, 3,409 retrieved pairs (+745 candidates over dHash-only); 52.3362 ms dHash scan and 86.179 ms CLIP scan.
- **2,000 combined:** 2,000/2,000 CLIP coverage, 1,999,000 dHash pairs in 641.8464 ms, 199,900 CLIP seed comparisons in 309.0918 ms; 11,960 retrieved pairs; proxy verification/hashing 16,073.4845 ms.
- **5,000 combined:** 5,000/5,000 CLIP coverage, 12,497,500 dHash pairs in 4,801.1025 ms, 499,900 CLIP seed comparisons in 821.0867 ms; 29,635 retrieved pairs; proxy verification/hashing 49,564.4522 ms; database read 1,690.3512 ms; 200 screened random negatives.

These are *retrieval* counts, not human-confirmed redundancy, held-out precision, archive-wide recall or automatic-suppression evidence. Exact vector evidence was **reused**, so the timings do not measure CLIP inference or model deployment. No protected WI-0193 human labels were reused.

**Change:** Explicit `--full-proxy-population` mode for the existing offline CLI, mutually exclusive with `--max-photos`, operates on every current, durable, proxy-backed photo revision. The same exact dHash scan has no date/person/location/folder restrictions. The CLIP scan still covers a *bounded set of deterministic seeds* (default 100) against every indexed vector in the selected population; CLIP recall for unseeded photos is not proved. Before loading private proxy bytes, reject populations exceeding **30,000 photos**, **450 million full-pair dHash comparisons**, or **16 GiB of declared proxy input**. Do not change the bounded default 500 or opt-in subset maximum 5,000.

Top-k insertion skips candidates which cannot outrank the existing worst entry (same distance/index tie-break); maintain exact ordering under dense comparisons. Add aggregate counts for all within-population dHash<=4 links, dHash candidates, CLIP candidates, CLIP-only pairs, and overlap. Preserve existing private sampler-compatible schema, deterministic selection, no source/score identifiers in stdout, exclusive private file creation, read-only CLIP transaction, and no original hydration/writes.

**After merge:** Operator first runs synthetic tests, then a single separately named full-proxy-population private export with pinned fingerprint and 100 CLIP seeds; reports only aggregate stdout. Stop if inventory/declared proxy size exceeds caps, if the machine experiences memory/storage pressure, or if proxy verification fails. Source paths, revision identities, scores, per-pair evidence and private exports remain local. Confirm complete proxy-backed cohort and exact timings before deciding whether a distinct broader CLIP candidate strategy, leakage diagnostics, sampling freeze or dedicated licensed model trial is justified. No labels, frozen thresholds, production use, or ANN assumption yet.


## Checkpoint 4 — full-proxy technical evidence and independent candidate preflight (2026-10-19; local preflight pending)

**Maintainer accepted checkpoint-3 feasibility:** all **27,050 current proxy-backed photos** scanned with exactly **365,837,725** dHash comparisons (25,910.6866 ms); all 27,050 have the pinned indexed CLIP vector; **100** CLIP seeds scanned against all other vectors (**2,704,900** comparisons, 4,514.1656 ms). Combined candidates: **162,829** (162,092 dHash; 799 seeded CLIP, including 737 CLIP-only and 62 overlapping); **2,495** strict dHash<=4 potential leakage links; 200 screened random negatives. PostgreSQL read 4,086.2912 ms; proxy verification/hashing 218,889.3549 ms. No labels, image hydration, catalogue writes, or threshold approval observed. Exact offline all-pair dHash is practical at this proxy-backed population; **ANN not currently warranted**. This does not validate candidate precision, exact-duplicate recall outside the candidate pool, broader unseeded CLIP recall or near-copy family completeness.

**Next bounded tool:** `redundancy_candidate_preflight.py` reads the saved private full-proxy candidate manifest and reuses the validated photo-disjoint partition logic in `redundancy_review_pairs.py`, without selecting review pairs or creating outputs. Only aggregate counts of exact content families, dHash<=4 leakage families, largest family sizes, cross-partition edges with dHash distances 5–8 or 9–12 or CLIP cosine >=0.90, candidate bucket eligibility and quota shortfalls may be printed. Known exact-copy and dHash<=4 edges must remain co-partitioned by construction. Add synthetic tests for stable aggregation, no labels, no identifiers in output and cross-partition warnings.

**Gate:** the preflight is an independent feasibility diagnostic, not a freeze attestation; the held-out human **review labels** have not been accessed and must remain unviewed. Do not create blind or labeled human redundancy reviews until cohort composition and leakage sensitivity are separately examined and the frozen protocol explicitly approved. Cross-partition similarity indicates *possible* near-copy leakage, not proven redundancy. WI-0195 stays blocked. See `docs/operations/visual-redundancy-research.md`.

## Checkpoint 5 — independent photo-disjoint linkage-sensitivity diagnostic (2026-10-19; local run pending)

Checkpoint-4 operator result passed four synthetic tests and analyzed the private full-proxy candidate export. The 27,050-photo / 163,029-pair inventory had 25,107 dHash<=4 leakage families, 1,700 multi-photo families, largest 37; calibration 20,149, held-out 6,901; no exact content control pairs in the available candidate inventory and no known dHash<=4 edges crossing partitions. **61,358** candidate pairs crossed, including **882 dHash5–8**, **3,867 dHash9–12**, and **1,596 CLIP>=0.90** candidates (overlap possible). The proposed held-out strata were short 41 borderline cases and 25 screened random negatives; both partitions lacked the exact-copy controls. This is **NO-GO for human review cohort freeze**. The edge counts represent possible missed leakage but not proof of redundancy.

Add `redundancy_partition_sensitivity.py`, an offline **aggregate-only read-only** report comparing dHash<=4 (exact current sampler membership), <=8, and <=12 **photo-disjoint grouping** with content SHA and supplied leakage groups always linked. Do not modify the actual pair sampler or output review manifests. Confirm baseline reproducibility, monotone family connection behavior, that a linked pair never crosses partitions, and that no human labels, pair identities or paths appear in diagnostics. For each scenario count partition sizes, family concentration, residual cross-partition weak dHash/CLIP edges, held-out photos incident to those *potential risk* edges and quotas/shortfalls across all seven predeclared candidate buckets. The expanded linkages are **sensitivity experiments**, not data-dependent suppression thresholds or model performance evidence; CLIP semantic similarity must not automatically co-partition distinct visual scenes. As partitions are reassigned when components change, differences between scenario counts cannot establish threshold performance.

**Gate:** Operator runs only the synthetic tests and private aggregate diagnostic; preserve the untouched private candidate manifest, and independently decide how to handle exact-copy controls, held-out borderline/negative shortages and unlinked potential near copies before any review freeze. No held-out redundancy labels have been read. WI-0195 remains blocked; production semantics unchanged. See the WI-0194 operations runbook.

## Checkpoint 6 — opt-in mid-similarity candidate supply (2026-10-19; local pending)

Maintainer checkpoint-5 sensitivity demonstrated **NO-GO for freeze**: dHash<=4 baseline largest linked family 37, <=8 created a 305-photo component, and <=12 created a 3,368-photo component (12.45% of current proxy-backed photos). Neither broader family isolation rule fixes missing true exact-copy controls (0), held-out borderline (19/60 under <=4 or <=8; 15/60 under <=12), or held-out screened random controls (10/35). Cross-partition risk links remain potential leakage indicators, not human redundancy labels. Keep <=4 only as an unfrozen research baseline; reject <=12 as a grouping strategy.

Extend the opt-in research-only evidence exporter with two strictly bounded controls. `--clip-borderline-per-seed` (default 0, max 16) additionally retrieves deterministic candidates from existing CLIP vectors in the exploratory cosine [0.62,0.78) range with exact existing dHash>24; these are mid-similarity *review candidates*, not ground truth, and cannot add dHash<=4 group edges. `--random-negative-controls` (default previous rule, max 1,000) expands screened potential negative controls without treating them as validated negatives; local trial requests 800. No prior private manifests are overwritten. Existing CLI defaults, exact model provenance, current-proxy-only reads, full-population guards, no originals/cloud hydration/catalogue mutation or production changes remain intact.

After PR merge, operator runs synthetic tests, one explicitly opted-in full-proxy candidate export to *new* private files, then the existing aggregate-only `redundancy_candidate_preflight.py` on the new candidate manifest. Only share aggregate stdout; do not inspect pair-level labels, freeze the cohort, infer precision or rerun consumed WI-0193 human-quality labels. True exact-copy controls are absent; never invent them. This is candidate-supply investigation, not human-review authorization. WI-0195 stays blocked.


## Checkpoint 7 — quarantine sensitivity on the enriched candidate cohort (2026-10-19; local acceptance pending)

**Checkpoint 6 accepted from maintainer aggregate output.** The full 27,050 current proxy-backed photo corpus retained complete indexed CLIP coverage and dHash<=4 leakage components unchanged (25,107 families, largest 37; 2,495 strict edges and no known strict cross-partition links). Opt-in candidate enrichment added **1,552 unique scored borderline pairs** from 1,582 attempts, plus **600** screened random-negative controls: 164,381 retrieved + 800 controls = **165,181** candidate/control pairs. Both previously short *held-out* review categories now exceed planned quotas: borderline **129/60** (previous 19), screened random-negative **51/35** (previous 10). Calibration borderline 975 and controls 430. Every quota is covered **except genuine exact-copy controls, zero in both partitions**. No exact copies may be manufactured or represented as actual archive duplicates. Runtime on current machine: verifying/hashing 7,446,014,452 bytes of proxies in 270,911.2531 ms; exact dHash 365,837,725 pairs in 28,492.362 ms; seeded CLIP 2,704,900 comparisons in 5,117.9886 ms.

**Cohort freeze remains blocked:** proposed baseline 20,149 calibration and 6,901 held-out photos has 882 cross-partition dHash5–8, 3,867 dHash9–12, and 1,596 high-CLIP (>=0.90) candidate edges (overlap possible). These represent *possible* leakage, not confirmed near-copy truth. Earlier <=8/<=12 union-linking had giant family sizes 305/3,368 without fixing prior quotas, so do not adopt either globally.

**New aggregate-only diagnostic** `redundancy_heldout_quarantine_preflight.py` reuses the existing private enriched manifest, strict dHash<=4 split, sampler bucket heuristics and existing candidate evidence. Compare four fixed **held-out-photo exclusion sensitivities**: none; incident to cross-partition dHash5–8; incident to dHash5–12; and incident to dHash5–12 **or** existing CLIP cosine >=0.90. For each, report triggering cross-partition candidate pairs, number of unique held-out photos excluded, remaining held-out photos/candidate pairs, all seven bucket eligibility counts, and shortfalls. **Never** repartition families, assign labels, create a new manifest, or access quality/redundancy held-out reviews. Add deterministic synthetic tests for nested exclusion monotonicity, membership stability, privacy-safe aggregation and label rejection.

This is a **diagnostic trade-off**, not permission to select the easiest held-out subset: similarity-based quarantining can bias evaluation, and 100 seeded CLIP queries plus retrieved edges cannot exclude unknown near copies. Even if enough cases remain, decide exact-copy controls explicitly as unavailable/not-applicable and independently review the protocol for representative held-out sampling and false-positive error coverage before authorizing any human review. No production changes; WI-0195 remains blocked.


## Checkpoint 8 — calibration-only pilot feasibility, no review authorization (2026-10-19; local pending)

**Maintainer checkpoint-7 passed as diagnostic, but all blanket held-out-photo quarantines are no-go.** Baseline enriched manifest: 27,050 current proxy-backed photos, 165,181 candidate/control pairs, unchanged strict dHash<=4 split (20,149 calibration; 6,901 held-out). No quarantine retains 11,843 held-out candidate pairs and 332 disagreement cases. Quarantining just held-out photos incident to cross-split dHash5–8 edges excludes **470** photos and cuts disagreement candidate supply to **38**, below quota 50. Quarantining dHash5–12 excludes **1,689** photos and leaves **7** disagreements. Adding CLIP>=0.90 excludes **2,055** photos (29.8%) and leaves **5** disagreements; borderline 51/60, screened negatives 23/35. Such filtering biases the very difficult examples needed for false-positive-suppression evaluation. These are similarity indicators, not proven near-copy leakage. Retain strict dHash<=4 as an unfrozen comparison baseline only. Global dHash<=8/12 grouping also remains rejected after the 305/3,368-photo giant components. No human reviews or frozen held-out cohort are authorized. True exact-content-copy candidates remain zero and must not be invented.

**Next diagnostic:** `redundancy_calibration_pilot_preflight.py` validates the already saved private enriched candidate JSON using existing deterministic photo-disjoint group membership. It evaluates an **unlabelled, calibration-only risk pilot supply** across predeclared mutually exclusive strata: strict dHash0–4, dHash-close/CLIP-low disagreement, dHash-far/CLIP-high disagreement, weak dHash5–8, weak dHash9–12, other high CLIP, measured CLIP mid/borderline, screened random controls and other candidates. It reports only eligible source counts and a deterministic **dry-run** of bounded candidate quotas, with at most two proposed pair occurrences per calibration photo. Any genuine exact-content copies are reported but have **zero pilot quota** until a separate explicit protocol exists. The pilot ignores all held-out-only and cross-partition pairs for eligibility; it accesses no human review labels and prints no identifiers/scores or pair manifests.

**Gate:** preflight result is a *proposal feasibility report*, not a blind list, not a label-collection authorization, and not a final held-out protocol. After checking the aggregate, separately specify protections against calibration-to-heldout weak-link contamination, a calibration-only reviewer workflow, explicit absence of exact-copy controls, and how a genuinely independent held-out set will be frozen **before any human labels or threshold fitting**. No product/ANN/collection changes; WI-0195 stays blocked.


## Integrated calibration pilot development phase (2026-10-19; local verification and optional review pending)

The maintainer requested a **substantial, bundled development phase instead of another chain of single-command preflight PRs**. Repository dependency check: WI-0192 and WI-0193 are completed; WI-0195 explicitly depends on an accepted WI-0194 redundancy result, so remains blocked. Continue WI-0194 without further mandatory micro-checkpoint/operator loops.

**Evidence accepted so far:** full 27,050 current durable proxy-backed revisions, 365,837,725 exact dHash comparisons with a practical offline scan; 27,050 matching existing CLIP vectors but only 100 query seeds, not full CLIP recall. Enriched candidate inventory: 165,181 pairs including 800 screened controls. The strict dHash<=4 split yields 20,149 calibration and 6,901 held-out photos with 25,107 linked families; all nine calibration-only pilot categories met their predeclared dry-run quotas: **200 pairs from 382 distinct calibration photos, zero held-out photos**. Broader <=8/12 leakage linkage formed huge connected components (305/3,368 photos). Selective quarantining of 470, 1,689, or 2,055 held-out photos depleted vital disagreement-case coverage (332 baseline to 38/7/5). **No representative frozen held-out, precision, or automatic suppression is approved.** Genuine exact-content-copy controls are absent and never manufactured.

**This integrated PR implements a complete *calibration-only*, non-production pilot workflow rather than another standalone preflight:**

1. `redundancy_pilot_package.py prepare`: deterministic selection of the same 200 calibration-only pairs using the existing nine mutually exclusive candidate strata, same SHA-bound scored manifest, source-fingerprint audit, pair-use cap 2 and score/model/partition-blinded display manifest. Outputs are exclusively created under `private/`, and existing enriched evidence is never overwritten. No held-out pair or label is eligible; no exact-copy controls are invented.
2. `redundancy pilot-proxies` .NET CLI: read-only PostgreSQL lookup for **current matching durable review proxies** for just the selected calibration revisions. Produces a new private, SHA-bound proxy locator. No original reads, OneDrive hydration, embedding inference, mutations or model changes.
3. `redundancy_pilot_review.py`: loopback-only browser with two photos side-by-side, four research labels (Redundant / Similar but keep both / Not similar / Unsure), optional notes, keyboard shortcuts, progress, and resumable private on-disk reviews. Proxy JPEG bytes are hashed and size-verified on every image request, with a fresh random local token and private paths not shown in UI.
4. `redundancy_pilot_package.py report`: SHA/audit/review-bound **calibration-only** aggregate label summaries by predeclared candidate bucket, counts of ambiguous/false-positive-type decisions, and descriptive Wilson intervals. It prints no per-photo identities, paths, labels or individual scores and explicitly disclaims held-out performance or production precision.
5. One Windows PowerShell entry point `run_redundancy_calibration_pilot.ps1` runs synthetic privacy/integrity/regression tests, makes or reuses private pilot artifacts, resolves proxies once, and optionally starts/reopens the reviewer with `-Review`; label summaries are printed on later runs. No repeated archive scan, no multiple manual data exports, and no need for further code/PR between preparation and actual calibration review.

**Acceptance gate:** This phase can be merged and prepared locally in one batch. **Calibration review is opt-in** and may be completed over several sessions; no review content is transferred off the machine. Future automatic suppression remains NO-GO unless an independent frozen, leakage-safe and representative held-out study meets very conservative precision evidence. At the end of calibration labeling, make one bundled decision: continue a genuinely independent held-out/model comparison in a separate substantive phase, or defer redundancy suppression and close WI-0194 honestly as a research no-go. Do not treat pilot Wilson intervals as a final result or reuse consumed WI-0193 quality labels. WI-0195 remains blocked pending accepted WI-0194 policy or documented no-go/defer interpretation.


## Bundled independent held-out study after 200 reviewed calibration pairs (2026-10-19; merge/local verification pending)

Maintainer completed all 200 calibration-only blind reviews from PR #616, with **82 Redundant, 17 Similar-but-keep-both and 101 Not-similar**, 0 Unsure (intentionally candidate-stratified, **not representative archive prevalence**). dHash0–4 and dHash5–8 candidates each produced 23/24 Redundant (Wilson95 lower about 0.798), whereas dHash9–12 yielded 8/24 and dHash-close/CLIP-low disagreements yielded 0/20. High-CLIP-other was 18/20, but dHash-far/CLIP-high disagreements were 9/20 with 11 similar-keep-both. These are useful *calibration signals*, but not an automatic suppression policy and not leakage-safe held-out evidence. Genuine exact-content controls remain unavailable, broad family linkage created giant components, and high-CLIP retrieval used only 100 seeds.

A **single substantive PR** now implements the next verification stage, rather than further small preflight PRs:
- `redundancy_validation_package.py prepare/report` pre-registers a **held-out-only** candidate-stratified pair cohort *before* held-out labels; reads the original calibration **blind** to protect its 382 photo endpoints, not calibration labels. Requires the unchanged private enriched candidate manifest. No recalibration of model cutoffs is performed.
- Quarantine is **limited to observed cross-partition dHash5–12 or CLIP>=0.90 candidate links whose calibration endpoint was actually used in the 200 reviewed pairs**, not the earlier harmful blanket-quarantine approach. It is explicitly acknowledged that this can still bias the held-out study and misses unobserved near copies. Gate rejects quarantine greater than 20% of held-out photos or fewer than 100 diversity-capped selected held-out candidate pairs. The gate must complete before writing private blind/audit files or showing held-out photos.
- The held-out pair list is bounded, uses a new deterministic seed and the previously declared nine mutually exclusive buckets, limits each held-out photo to two selected pairs, and honestly reports shortfalls. Separate new private audit is SHA-bound to the frozen blind, records risk exclusions and absence of score/threshold production approval. True archive exact duplicates are not fabricated.
- Extend existing .NET current-proxy locator with **mandatory `--validation-audit` for held-out manifests**, reusing its read-only DB/proxy and byte-hash integrity, and extend the loopback side-by-side reviewer only behind explicit held-out mode. Calibration paths and labels continue to use their original contracts. Held-out label file is separate, never mixed with calibration or WI-0193 quality data.
- Ship regression tests plus a single Windows PowerShell runner: default performs one synthetic suite and prepares/checks frozen cohort/proxies, **not review**; `-Review` alone opens the guarded, bound, resumable held-out reviewer and report. No full-archive re-export, fresh inference, database mutation or production effect.

**Decision boundary:** completed calibration confirms some promising per-pair ordering, but **no automatic dHash or CLIP suppression rule qualifies today**. The next collected held-out outcomes are **descriptive under selection**; they cannot establish whole-archive precision, and must not be used to tune and rerun against the same cohort. On completion, make one evidence-based go/no-go/defer decision; if insufficient, stop and address WI-0195's dependency through an explicit milestone decision rather than more tiny checkpoints.

## Verification requirements

Reproducible neighbour generation, deterministic review-set export, maintainer pair/group review and privacy-safe aggregate threshold/model evidence.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:

## Final research disposition — 2026-10-21

**WI-0194 completed as a visual redundancy EVALUATION, not production
automatic suppression.** The maintained archive research
covered 27,050 current proxies and 165,181 enriched
candidate/control relationships, with 200 calibration and 180 guarded
held-out human pair reviews. Positive dHash-close and high-CLIP
candidate strata are interesting for **future manual suggestions**,
but none met a defensible conservative deployment precision
standard. Heldout photo-correlation, selected candidate strata,
quarantine-related bias, uncovered far-dHash/high-CLIP
disagreements and incomplete true-copy controls rule out a
safe automatic suppression threshold. **NO-GO to unattended
redundancy suppression.** All source originals and production
collection behavior unchanged. The operator's human-controlled
suggestion goal continues separately under issue #629.
