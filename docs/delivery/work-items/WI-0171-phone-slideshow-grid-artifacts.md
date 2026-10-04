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

On 2026-10-01 the maintainer reproduced the artifact in the installed PWA on a Google Pixel 9 Pro XL and supplied screenshots. The lines are not visible on the maintained PC. They were only observed for some prepared originals. In the supplied paused screenshots the seams form straight horizontal/vertical lines at screen-aligned positions rather than following photo content; when playback is resumed the lines flicker as the subtle scale motion runs. The private screenshots are verification evidence only and are not committed to the repository.

This evidence points to a phone compositor/raster-tiling artifact rather than lines encoded in the source image: prepared playback serves the full-resolution original and the foreground presentation normally applies a continuously scaled transform. The failure is therefore scoped to the decorative transform, not original preparation or image bytes.

## Acceptance criteria and verification

1. Record phone model/browser and a screenshot or short recording showing the lines.
2. Compare Prepare originals on/off for the same photo and note whether lines occur within the photo or backdrop, during motion/fade or while paused.
3. Implement a targeted fix, verify on the affected phone and retain desktop presentation behavior.

## Implementation

The slideshow presentation stylesheet now suppresses only the decorative foreground scale transform when all of these are true:

- the foreground resource is a prepared-original playback URL;
- the browser reports a coarse primary pointer; and
- the browser reports no hover capability, matching phone/tablet presentation surfaces.

The fallback leaves the prepared original itself, backdrop, playback timing and opacity cross-fade intact. Proxy playback continues to use the existing subtle motion, and desktop/mouse presentation behavior is unchanged. This avoids keeping a full-resolution original on the transformed compositor layer that exhibited tile seams on the affected Pixel while preserving the accepted presentation effects everywhere that has not reproduced the problem.

No new host-heavy test or CI gate is justified for this change: the defect is a physical-device GPU/compositor artifact that the existing DOM/unit layers cannot reproduce. Normal Web build/CI remains the automated guard; final acceptance must be performed on the affected phone.

## Status

Implementation is ready for maintained verification. Retest one of the affected photos on the Pixel 9 Pro XL with Prepare originals enabled while paused, playing and during a cross-fade, then compare the same photo with Prepare originals disabled. Confirm desktop playback still retains the normal subtle scale motion and no grid seams.

## Maintainer acceptance — 2026-10-04 (Europe/Stockholm)

The maintainer repeated the WI-0171 phone/PWA verification and reports that the fix works as expected. The previously observed thin grid/compositor seams no longer block acceptance. WI-0171 is complete.
