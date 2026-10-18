---
id: WI-0199
title: Prototype full-photo then subject-detail sequence
milestone: M38
status_source: PhotoIdentity.Docs
depends_on: [WI-0198]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0199: Prototype full-photo then subject-detail sequence

## Objective

Prototype showing a full portrait first and then a detail crop, allowing sofa-distance viewers to see the scene and recognize faces/activities without permanently losing context.

## In scope

- Experimental variation of opt-in TV Focus; show contained full image then safe enlarged detail using the same logical photo.
- Define a bounded, intelligible split of current effective image duration; measure whether longer duration is preferable rather than silently doubling it.
- Preserve slideshow snapshot index, one exposure per revision, predecode readiness, smooth transitions, pause/resume at either stage, skip, loop and captions.
- Support reduced motion with static stages or Standard fallback; no forced camera animation.
- Reuse the underlying image/resource and avoid increasing retained media beyond a measured bound.

## Out of scope

Saving additional photo revisions/exposures, changing collection ordering or automatically making this variant the default.

## Acceptance criteria

- [ ] One image remains one slideshow item and one exposure regardless of stages.
- [ ] Stage timing/skip/pause/loop/failure semantics have focused regression tests and remain predictable.
- [ ] Any enlarged detail satisfies WI-0197 crop safety; unsafe images remain contained.
- [ ] Reduced-motion and low-powered browser fallbacks do not flash, stall or crop unsafe content.
- [ ] Real TV pilot compares scene comprehension, face legibility and distraction with static TV Focus.

## Verification

Automated timer, navigation and transition tests plus private TV/phone sample review.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
