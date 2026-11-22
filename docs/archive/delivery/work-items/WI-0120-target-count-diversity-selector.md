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

- [x] A Creative Collection accepts a validated target count and returns no more than the available unique candidates.
- [x] Candidate sets smaller than the target remain valid and do not duplicate or invent photos.
- [x] Large candidate sets are reduced deterministically toward the target count.
- [x] Selection gives explicit value to temporal and moment coverage and applies a documented diminishing-return penalty to repetitive near-consecutive material.
- [x] Anchor/context provenance survives selection so the final result can report how many direct matches and contextual additions remain.
- [x] A private high-volume collection shows measurably broader temporal/moment coverage than simple first-N or uniform chronological truncation at the same target size.
- [x] A private burst/repetition-heavy collection contains fewer obviously repetitive selections than an uncurated candidate list at the same size.
- [x] The selector does not require location metadata and remains useful when all candidates lack GPS/place data.
- [x] Same candidates, target and policy/version produce the same selected revision IDs and final order.
- [x] The final immutable revision sequence can be materialized through the existing slideshow snapshot contract so playback receives fixed revision IDs instead of live selection state.
- [x] Automated tests cover target below/above candidate count, deterministic ties, temporal balancing, moment diversity, repeated-person sets, context retention and zero candidates.

## Verification requirements

Automated tests are required for deterministic selection, target bounds, diversity rules and slideshow snapshot integration. Human maintainer verification is required across at least one large, one small and one repetition-heavy private collection, comparing curated output with the uncurated baseline.

## Completion notes

- Files changed: `src/PhotoIdentity.Core/Collections/CreativeCollectionSelection.cs`, `src/PhotoIdentity.Core/Collections/ISmartCollectionQueryRepository.cs`, SQLite/PostgreSQL Smart Collection query repositories, `src/PhotoIdentity.Api/CreativeCollectionPreviewEndpoints.cs`, Core/integration tests, and `docs/operations/creative-collection-preview.md`.
- Trade-offs: `m26-target-diversity-balanced-v1` deliberately uses inspectable metadata weights rather than image embeddings or aesthetic scoring. It rewards first coverage of moments, time-span buckets and identified-person combinations, gives direct anchors a relevance preference, permits context to survive when it adds diversity, and penalizes repeated moments/people combinations plus captures within two minutes of already selected photos. Final ordering remains chronological. Identified people are added only as internal query evidence; existing public Smart Collection page contracts remain unchanged.
- Maintainer verification: on the private high-volume sample, the chronological baseline covered 6 distinct days and 5 months with 40 adjacent pairs within two minutes, while Creative selected 50 covered 48 days and 22 months with zero adjacent pairs within two minutes, spanning 50 distinct inferred moments and 19 identified-person combinations. On a repetition-heavy 84-photo sample, selection reduced adjacent pairs within two minutes from 55 to 21 while selecting 50 photos across four inferred moments. A 14-photo sample remained 14 unique photos when the requested target was 50. A 50-photo Creative slideshow snapshot matched the preview selection exactly by revision ID and order.
- Follow-on: WI-0121 owns the user-facing Creative recipe/preview/launch surface rather than adding another slideshow menu option here.
- Commands run: GitHub Actions validation on PR #357, PostgreSQL materialization fix in PR #358, and maintainer private verification completed on 2026-09-18.
