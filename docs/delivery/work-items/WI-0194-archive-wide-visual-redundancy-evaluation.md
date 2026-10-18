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

## Verification requirements

Reproducible neighbour generation, deterministic review-set export, maintainer pair/group review and privacy-safe aggregate threshold/model evidence.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
