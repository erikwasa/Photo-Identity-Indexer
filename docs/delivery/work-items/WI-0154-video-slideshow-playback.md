---
id: WI-0154
title: Integrate video clips into slideshow playback
milestone: M30
status_source: ../status/work-items.yaml
depends_on: [WI-0152, WI-0139]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, docs]
---

# WI-0154: Integrate video clips into slideshow playback

## Objective

Allow mixed photo/video collections to play through the slideshow experience with predictable timing and controls.

## Why

Family slideshows are the main consumption path where catalogued videos would otherwise remain conspicuously excluded.

## In scope

- Define clip duration/bounded playback policy and advancement.
- Reuse fullscreen/no-fullscreen, protected controls and wake behavior.
- Stage poster/video readiness without charging unrelated photo timing.
- Handle muted/autoplay restrictions explicitly.
- Keep resource use bounded in long mixed loops.

## Out of scope

- Automatic highlight extraction.
- Complex trimming/editing.
- Forcing autoplay against browser policy.

## Acceptance criteria

- [ ] Mixed collection advances photo -> video -> photo safely.
- [ ] Long-clip policy is deterministic/documented.
- [ ] Autoplay/fullscreen restrictions degrade actionably rather than deadlock.
- [ ] Image-only slideshow behavior remains unchanged.

## Verification requirements

Automated mixed-media playback tests plus phone/desktop acceptance.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
