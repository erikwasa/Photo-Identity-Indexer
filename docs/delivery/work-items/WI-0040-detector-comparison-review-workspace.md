---
id: WI-0040
title: Build a viewport-fitted detector comparison review workspace
milestone: M16
status_source: ../status/work-items.yaml
depends_on: [WI-0039]
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Integration.Tests]
---

# WI-0040: Build a viewport-fitted detector comparison review workspace

## Objective

Make detector-comparison exception review predictable across portrait, landscape and unusually large photos so the operator can see the complete image, box markers, decision controls and save/navigation actions without repeatedly scrolling the browser page up and down.

## Context

The comparison page now reviews one photo at a time and uses compact `R` and `C` box markers. The remaining usability problem is that the source image still renders at full available width with aspect-ratio-driven height. Tall photos can push the decision controls below the viewport, while short photos leave unused space. The operator may need to alternate between the image and controls several times for one decision.

The preferred design is a dedicated review workspace rather than another fixed image-height adjustment. On desktop, the image and decisions should remain visible side by side inside a viewport-bounded area. Overflow decisions may scroll independently without moving the image. Detail inspection remains available through explicit zoom and pan controls.

## Scope

- Introduce a responsive review workspace for the current exception photo.
- On supported desktop widths, place a viewport-fitted image viewer beside the decision panel.
- Keep the complete image visible by default with `object-fit: contain` or equivalent letterboxing rather than cropping.
- Give the workspace a stable viewport-relative height so changing photo aspect ratio does not change the document position.
- Keep previous/next navigation and completion/save actions visible within the workspace.
- Allow the decision panel to scroll independently when a photo has more decisions than fit vertically; page-level scrolling must not be required during the normal photo-to-photo review loop.
- Add explicit `Fit`, zoom-in, zoom-out and reset controls, plus panning while zoomed.
- Preserve normalized reference and candidate overlay geometry at every zoom level.
- Reset the image to `Fit`, the decision panel to its top and transient field focus when moving to another photo.
- Link overlay markers and decision rows so hover, focus or activation clearly highlights the corresponding `R` or `C` box and control.
- Keep comparison metrics, summaries, instructions and qualitative-gate controls available outside or in a collapsible details area without consuming the active review viewport.
- Provide a narrow-screen fallback with a bounded image, stacked decisions and continuously reachable save/navigation actions.
- Preserve the existing comparison API, persisted correction request and metric semantics.

## Interaction rules

- `Fit` is the default for every photo and always shows the complete source image.
- Zoom is temporary inspection state; it is not carried to the next or previous photo.
- Zooming must not change any saved bounding-box coordinates or matching decisions.
- Scrolling a long decision list must leave the image and its overlays visible.
- Selecting or focusing a decision row must make its corresponding image marker visually distinct.
- Selecting a marker should focus or reveal the corresponding decision when that marker requires manual action.
- `Save and next` keeps the existing completeness gate and opens the next photo in the default fitted state.

## Acceptance criteria

- [ ] At representative desktop viewports, including `1280 × 720` and `1440 × 900`, the current photo, complete fitted image, overlay legend, navigation and save actions are visible without page-level scrolling.
- [ ] Portrait, landscape and square source images use the same stable review-workspace footprint and are never cropped in the default fitted state.
- [ ] When decision content exceeds the available height, only the decision panel scrolls and the image remains visible.
- [ ] Previous, next, save and save-and-next controls remain reachable throughout review.
- [ ] Fit, zoom in, zoom out and reset work without changing normalized overlay alignment.
- [ ] Moving to another photo resets zoom, pan and decision-panel scroll position.
- [ ] Overlay-to-control highlighting works for both reference faces and candidate detections and is usable by keyboard focus as well as pointer interaction.
- [ ] The narrow-screen layout bounds the image height and does not allow the image to consume the full page before controls become reachable.
- [ ] Existing correction persistence, automatic-miss behavior, completion arithmetic, metrics and exports remain unchanged.
- [ ] Automated coverage verifies view-state reset and overlay transform behavior; review-application smoke verification covers representative desktop and narrow layouts.

## Out of scope

- Changing detector matching, IoU rules, ground-truth semantics or gate arithmetic.
- Editing reference or candidate box geometry from the comparison page.
- Persisting zoom or pan state across photos or sessions.
- Replacing the existing source-image endpoint or correction-storage format.

## Completion evidence

Record the implementation pull request, passing build and review-application smoke verification, and privacy-safe human verification that a representative portrait photo, landscape photo and multi-decision photo can be reviewed without page-level back-and-forth scrolling.
