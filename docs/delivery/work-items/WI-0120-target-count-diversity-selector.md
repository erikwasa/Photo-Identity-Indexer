---
id: WI-0120
title: Add target-count and diversity selection for Creative Collections
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0119, WI-0083]
related_adrs: [ADR-0009]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Core.Tests, PhotoIdentity.Persistence.Tests, PhotoIdentity.Integration.Tests, docs]
---

# WI-0120: Add target-count and diversity selection for Creative Collections

## Objective

Select a requested, best-effort number of photos from Creative Collection anchor/context candidates while reducing repetition and improving coverage across time, inferred moments, people combinations and contextual views, then produce a stable slideshow-ready revision sequence.

## Why

Exact queries routinely produce result sets that are either too large for an enjoyable slideshow or too small to feel complete. Adding more hard filters only changes membership; it does not solve presentation quality. A selector should therefore optimize the collection as a whole instead of showing every candidate or taking the first N photos.

The initial selector should be explainable and metadata-first. It does not need image embeddings or an opaque aesthetic model to prove the value of target sizing and diversity.

## In scope

- Accept the anchor/context candidate set from WI-0119 and a requested target photo count.
- Treat target count as best effort: never duplicate photos to reach it, and use all suitable candidates when fewer exist.
- Preserve anchor relevance while allowing context photos to survive when they add distinct presentation value.
- Reward temporal coverage and inferred-moment coverage instead of letting high-volume periods dominate purely by photo count.
- Penalize obvious repetition using available metadata such as very small capture-time gaps, repeated moment membership and repeated identified-person sets.
- Reward useful diversity such as a new moment, underrepresented period, different people combination or context view.
- Define deterministic scoring/selection and stable tie-breaking for the same candidate set and policy version.
- Keep chronological playback as the initial final ordering after selection, unless evaluation establishes a clearer deterministic ordering rule.
- Surface enough selection explanation/summary to compare the curated output with all candidates during maintainer evaluation.
- Materialize the final immutable revision list through the established slideshow snapshot/playback boundary rather than turning playback into a live selector.
- Evaluate at least large-result, small-result and highly repetitive private collections.

## Out of scope

- General-purpose recommendation ranking across the whole archive.
- Image-aesthetic scoring, face-expression scoring or semantic/visual embeddings.
- Music selection, transitions, captions or generated narrative text.
- Guaranteeing an exact target when fewer suitable candidates exist.
- Replacing exact/Classic Smart Collection slideshow behavior; Creative Collections are an additional presentation path.
- Learning personal taste automatically from usage history in this work item.

## Acceptance criteria

- [ ] A Creative Collection accepts a validated target count and returns no more than the available unique candidates.
- [ ] Candidate sets smaller than the target remain valid and do not duplicate or invent photos.
- [ ] Large candidate sets are reduced deterministically toward the target count.
- [ ] Selection gives explicit value to temporal and moment coverage and applies a documented diminishing-return penalty to repetitive near-consecutive material.
- [ ] Anchor/context provenance survives selection so the final result can report how many direct matches and contextual additions remain.
- [ ] A private high-volume collection shows measurably broader temporal/moment coverage than simple first-N or uniform chronological truncation at the same target size.
- [ ] A private burst/repetition-heavy collection contains fewer obviously repetitive selections than an uncurated candidate list at the same size.
- [ ] The selector does not require location metadata and remains useful when all candidates lack GPS/place data.
- [ ] Same candidates, target and policy/version produce the same selected revision IDs and final order.
- [ ] The final immutable revision sequence can be launched through the existing slideshow lifecycle without changing the running session when catalogue/filter data later changes.
- [ ] Automated tests cover target below/above candidate count, deterministic ties, temporal balancing, moment diversity, repeated-person sets, context retention and zero candidates.

## Verification requirements

Automated tests are required for deterministic selection, target bounds, diversity rules and slideshow snapshot integration. Human maintainer verification is required across at least one large, one small and one repetition-heavy private collection, comparing curated output with the uncurated baseline.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
