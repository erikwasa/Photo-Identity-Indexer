---
id: WI-0171
title: Diagnose thin grid-like lines during phone slideshow playback
milestone: M32
status_source: ../status/work-items.yaml
depends_on: []
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0171: Diagnose thin grid-like lines during phone slideshow playback

## Objective and maintainer evidence

Maintainer reports very thin grid-like lines during phone playback. The stdout log cannot reveal raster/compositor artifacts. Reproduce on the maintained phone, compare proxy vs original and moving vs static/fading frames, and determine whether lines are encoded in the served image or created by rendering. Do not change accepted presentation effects without evidence.

Reported 2026-09-30 (Europe/Stockholm) during M32 acceptance.

## Acceptance criteria and verification

1. Record phone model/browser and a screenshot or short recording showing the lines.
2. Compare Prepare originals on/off for the same photo and note whether lines occur within the photo or backdrop, during motion/fade or while paused.
3. Implement a targeted fix, verify on the affected phone and retain desktop presentation behavior.

## Status

Maintained desktop/phone verification remains outstanding.
