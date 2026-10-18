---
id: WI-0200
title: Prototype simultaneous context-and-detail layout
milestone: M38
status_source: PhotoIdentity.Docs
depends_on: [WI-0198]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0200: Prototype simultaneous context-and-detail layout

## Objective

Prototype a restrained landscape-TV layout showing a fully contained portrait beside a magnified subject detail, then compare it with single-image approaches.

## In scope

- On wide displays, allocate a context pane for the full photo and a detail pane using the safe WI-0197 crop of the same image.
- Test whether the detail actually becomes more legible from a sofa while preserving scene context.
- Prevent panel overlap with existing captions, timer/parent controls and crossfade layers.
- Bound duplicate image DOM/decode/GPU costs, keep stable A/B transitions and avoid extra permanent derivatives.
- Fallback to contained or single-image presentation on landscape phones, too-small viewports, unknown subjects or insufficient source resolution.

## Out of scope

AI outpainting, synthetic image content, per-photo layouts, heavy ML, additional saved media or an authoring UI.

## Acceptance criteria

- [ ] Both contained whole scene and enlarged detail are visible on suitable 16:9 TVs without known face clipping.
- [ ] Small screens, missing or unsafe geometry and low-quality content revert to a documented safe layout.
- [ ] Captions, parent controls and progress remain legible and unobtrusive.
- [ ] Transition/render memory and network behavior stay bounded through long and rapidly navigated slideshows.
- [ ] TV evaluation separates detail legibility from mere screen coverage and visual distraction.

## Verification

Real TV prototype comparison, phone fallback review and bounded playback diagnostics.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
