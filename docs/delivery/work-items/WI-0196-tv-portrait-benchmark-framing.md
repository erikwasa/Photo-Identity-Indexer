---
id: WI-0196
title: Define TV portrait-viewing benchmark and safe-framing rules
milestone: M38
status_source: PhotoIdentity.Docs
depends_on: []
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0196: Define TV portrait-viewing benchmark and safe-framing rules

## Objective

Establish a repeatable private-photo benchmark and explicit photo-framing safety rules before changing rendering.

## In scope

- Assemble approximately 30-50 fixed private revisions: single people, pairs, groups, full-body, activities, off-center/edge subjects, tight shots, no-face scenes, bright/dark pictures, low-resolution proxies, and landscape controls. Include 9:16, 3:4, and unusual aspect ratios.
- Record accepted contain + adaptive backdrop as baseline on an actual 16:9 TV at normal sofa distance; document display size/resolution, browser, lighting, distance, and landscape-phone test conditions without private identifiers.
- Evaluate candidate 1.4x, 1.6x and 2x enlargement as hypotheses, not default settings. Judge subject/detail legibility AND discarded photographic context.
- Define a conservative face-safe framing contract: all known faces protected with margin, wide groups/tight/edge subjects fall back, absence of detection evidence distinguished from verified zero-face evidence, no arbitrary center crop on unknown content.
- Consider source resolution and quality, especially 1600-pixel-long-edge review proxies versus available prepared originals. Do not hydrate sources implicitly.
- Design anonymized repeatable review scoring for legibility, crop safety, context loss, softness, comfort/distraction and preference.

## Out of scope

Production UI or rendering changes, new ML detectors, image derivatives, original editing, and publishing personal files or paths.

## Acceptance criteria

- [ ] Representative fixed evaluation set and reproducible viewing-distance protocol are documented without private photo details.
- [ ] The Standard baseline and its TV legibility limits are recorded, not just screen-coverage percentages.
- [ ] Crop-safety bounds cover missing/invalid geometry, zero/one/multiple faces, edge/tight framing, no-face pictures and low pixel density.
- [ ] Candidate scale bounds and fallback requirements are measurable and suitable for synthetic automated tests.
- [ ] Review protocol can be reused unchanged for the other M38 work items.

## Verification

Private TV and landscape-phone baseline plus synthetic safety test-case design; commit aggregate evidence only.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
