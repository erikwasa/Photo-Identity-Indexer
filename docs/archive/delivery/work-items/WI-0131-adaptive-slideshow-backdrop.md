---
id: WI-0131
title: Add a restrained adaptive backdrop for contained slideshow photos
milestone: M27
status_source: ../status/work-items.yaml
depends_on: [WI-0129]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Web.Tests, docs]
---

# WI-0131: Add a restrained adaptive backdrop for contained slideshow photos

## Objective

Reduce the sterile black-letterbox feeling for portrait/landscape mismatch by adding a restrained photographic backdrop behind the fully legible contained foreground image.

## Why

The current black fullscreen surface is safe and predictable, but large unused areas can make portrait photos on landscape screens (and vice versa) feel visually detached. A subdued backdrop can make the whole screen feel connected to the photograph without changing the source image.

## In scope

- Prototype an enlarged same-photo backdrop behind the existing contained foreground image.
- Apply strong blur/dimming/softening so the backdrop reads as ambience rather than a second competing image.
- Preserve a neutral/black fallback for performance, image failure, unsupported effects or poor visual results.
- Avoid additional network fetches when the browser can reuse the same resource.
- Ensure backdrop changes follow the WI-0129 staging/transition lifecycle without independent flashes.
- Keep effects GPU/memory conscious on mobile.
- Do not add a normal slideshow setting solely to choose backdrop style.

## Out of scope

- Cropping the foreground photo to fill the viewport.
- Dominant-color extraction infrastructure unless needed by a later measured fallback.
- User-authored themes/backgrounds.

## Acceptance criteria

- [x] Mixed-aspect-ratio photos retain an uncropped/contained foreground presentation.
- [x] The backdrop cannot obscure or materially reduce contrast of the foreground photo.
- [x] Backdrop transition is synchronized with foreground transition and does not flash the outgoing/incoming image incorrectly.
- [x] The implementation has a cheap neutral fallback and can disable expensive effects on unsuitable devices/browsers.
- [x] Long playback remains bounded in DOM/image resources.
- [x] Maintainer comparison on representative family photos records whether the treatment should ship as default, be simplified, or be rejected.

## Verification requirements

Phone/browser review across portrait-on-landscape, landscape-on-portrait, dark images, bright images and low-resolution proxies. Include performance observation on at least one constrained/mobile device.

## Completion notes

- Files changed: the dual-layer slideshow presentation component/CSS, presentation transition JavaScript, focused JavaScript regression coverage, and M27 delivery status/handoff documents.
- Trade-offs: each bounded A/B presentation slot now contains the normal `object-fit: contain` foreground plus one same-resource `object-fit: cover` backdrop. The backdrop is heavily blurred, dimmed and desaturated, with an additional shade; the foreground remains uncropped and visually above it. This adds one decorative image element per slot but does not add another slideshow generation or unbounded lifecycle.
- Fallback: the backdrop can be disabled through the internal component parameter without adding a viewer setting. Unsupported CSS filter, reduced-data preference, or forced-colors mode suppresses the expensive backdrop and leaves the existing black layer background.
- Transition behavior: opacity is now applied to the entire A/B presentation layer while decode/readiness diagnostics remain attached to the foreground image, so foreground and backdrop enter/leave together. A JavaScript regression test covers this split.
- Maintainer verification: accepted on 2026-09-18 after representative desktop/phone review; the adaptive backdrop, synchronized transitions and playback performance worked as expected, so the treatment is accepted as the default.
- Commands run: implementation passed repository CI before merge; maintainer desktop/phone acceptance completed on 2026-09-18.
