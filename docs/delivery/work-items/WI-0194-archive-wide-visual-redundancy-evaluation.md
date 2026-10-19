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


## Checkpoint 3 — guarded full-proxy-population exact retrieval (2026-10-19; local acceptance pending)

The maintainer's read-only archive probes succeeded on deterministic current-catalogue samples:
- **500 dHash-only:** 27,050 proxy-backed current photos, 500 sampled, 124,750 exact dHash comparisons, 2,664 retrieved pairs, 100 filtered negatives; 47.3782 ms exact scan; 4,423.0172 ms proxy verification/hashing.
- **500 dHash + exact-fingerprint pinned CLIP:** 500/500 CLIP coverage, 49,900 seed comparisons, 3,409 retrieved pairs (+745 candidates over dHash-only); 52.3362 ms dHash scan and 86.179 ms CLIP scan.
- **2,000 combined:** 2,000/2,000 CLIP coverage, 1,999,000 dHash pairs in 641.8464 ms, 199,900 CLIP seed comparisons in 309.0918 ms; 11,960 retrieved pairs; proxy verification/hashing 16,073.4845 ms.
- **5,000 combined:** 5,000/5,000 CLIP coverage, 12,497,500 dHash pairs in 4,801.1025 ms, 499,900 CLIP seed comparisons in 821.0867 ms; 29,635 retrieved pairs; proxy verification/hashing 49,564.4522 ms; database read 1,690.3512 ms; 200 screened random negatives.

These are *retrieval* counts, not human-confirmed redundancy, held-out precision, archive-wide recall or automatic-suppression evidence. Exact vector evidence was **reused**, so the timings do not measure CLIP inference or model deployment. No protected WI-0193 human labels were reused.

**Change:** Explicit `--full-proxy-population` mode for the existing offline CLI, mutually exclusive with `--max-photos`, operates on every current, durable, proxy-backed photo revision. The same exact dHash scan has no date/person/location/folder restrictions. The CLIP scan still covers a *bounded set of deterministic seeds* (default 100) against every indexed vector in the selected population; CLIP recall for unseeded photos is not proved. Before loading private proxy bytes, reject populations exceeding **30,000 photos**, **450 million full-pair dHash comparisons**, or **16 GiB of declared proxy input**. Do not change the bounded default 500 or opt-in subset maximum 5,000.

Top-k insertion skips candidates which cannot outrank the existing worst entry (same distance/index tie-break); maintain exact ordering under dense comparisons. Add aggregate counts for all within-population dHash<=4 links, dHash candidates, CLIP candidates, CLIP-only pairs, and overlap. Preserve existing private sampler-compatible schema, deterministic selection, no source/score identifiers in stdout, exclusive private file creation, read-only CLIP transaction, and no original hydration/writes.

**After merge:** Operator first runs synthetic tests, then a single separately named full-proxy-population private export with pinned fingerprint and 100 CLIP seeds; reports only aggregate stdout. Stop if inventory/declared proxy size exceeds caps, if the machine experiences memory/storage pressure, or if proxy verification fails. Source paths, revision identities, scores, per-pair evidence and private exports remain local. Confirm complete proxy-backed cohort and exact timings before deciding whether a distinct broader CLIP candidate strategy, leakage diagnostics, sampling freeze or dedicated licensed model trial is justified. No labels, frozen thresholds, production use, or ANN assumption yet.

## Verification requirements

Reproducible neighbour generation, deterministic review-set export, maintainer pair/group review and privacy-safe aggregate threshold/model evidence.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
