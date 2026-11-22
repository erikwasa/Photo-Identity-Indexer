---
id: WI-0040
title: Build a viewport-fitted detector comparison review workspace
milestone: M16
status_source: ../status/work-items.yaml
depends_on: [WI-0039]
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Api, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Integration.Tests]
---

# WI-0040: Build a viewport-fitted detector comparison review workspace

## Objective

Make detector-comparison exception review predictable across portrait, landscape and unusually large photos so the operator can see the complete image, box markers, decision controls and save/navigation actions without repeatedly scrolling the browser page up and down.

## Context

The comparison page reviews one exception photo at a time and uses compact `R` and `C` box markers. The previous vertically flowing layout allowed source-image aspect ratio to determine page height, which made portrait and unusually large photos force repeated browser-page scrolling between overlays and decisions.

The delivered design uses a dedicated viewport-bounded review workspace. On desktop, the complete fitted image and decisions remain visible side by side. Overflow decisions scroll independently without moving the image, while explicit zoom and pan controls support detailed inspection.

## Completion status

WI-0040 was implemented in PR #79 and completed on 2026-08-05 after the maintainer tested the merged workflow and confirmed that it works as expected.

PR #80 fixed a discovered cross-catalogue image-resolution defect before completion was recorded. Saved comparisons now use comparison-scoped image URLs and can resolve the verified source image after restarting against the baseline or another isolated candidate catalogue. Existing comparisons do not need to be recreated.

Delivered behavior includes:

- a viewport-bounded split review workspace with continuously reachable navigation and save actions;
- complete-image fitting by both available width and height;
- Fit, 100%, 200%, 400%, zoom-step and drag-to-pan controls;
- independent decision-panel scrolling;
- per-photo reset of zoom, pan, decision-panel scroll and transient review focus;
- pointer and keyboard linkage between `R`/`C` overlays and decision controls;
- collapsed comparison-level metrics, summaries, instructions and gate assessment below the active workspace;
- a bounded narrow-screen fallback with sticky save actions;
- automatic detector-miss handling for candidate-free photos;
- comparison-scoped source-image retrieval across isolated catalogues using staged filename and full frozen SHA-256; and
- automated view-state, published asset, responsive-style and cross-catalogue content coverage.

## Scope

- Introduce a responsive review workspace for the current exception photo.
- On supported desktop widths, place a viewport-fitted image viewer beside the decision panel.
- Keep the complete image visible by default with letterboxing rather than cropping.
- Give the workspace a stable viewport-relative height so changing photo aspect ratio does not change the document position.
- Keep previous/next navigation and completion/save actions visible within the workspace.
- Allow the decision panel to scroll independently when a photo has more decisions than fit vertically.
- Add explicit Fit, zoom-in, zoom-out and reset controls, plus panning while zoomed.
- Preserve normalized reference and candidate overlay geometry at every zoom level.
- Reset the image to Fit, the decision panel to its top and transient field focus when moving to another photo.
- Link overlay markers and decision rows so hover, focus or activation highlights the corresponding `R` or `C` box and control.
- Keep comparison metrics, summaries, instructions and qualitative-gate controls available in a collapsible details area without consuming the active review viewport.
- Provide a narrow-screen fallback with a bounded image, stacked decisions and continuously reachable save/navigation actions.
- Preserve the existing correction request and metric semantics.
- Keep comparison photos available after switching between isolated catalogues when the staged filename and complete frozen SHA-256 still match.

## Interaction rules

- Fit is the default for every photo and always shows the complete source image.
- Zoom is temporary inspection state and is not carried to the next or previous photo.
- Zooming does not change saved bounding-box coordinates or matching decisions.
- Scrolling a long decision list leaves the image and its overlays visible.
- Selecting or focusing a decision row makes its corresponding image marker visually distinct.
- Selecting a marker focuses or reveals the corresponding manual decision.
- Candidate-free reference faces are counted as detector misses automatically and are persisted through the normal save workflow.
- Save and next retains the completeness gate and opens the next photo in the default fitted state.

## Acceptance criteria

- [x] At representative desktop viewports, including `1280 × 720` and `1440 × 900`, the current photo, complete fitted image, overlay legend, navigation and save actions are visible without page-level scrolling.
- [x] Portrait, landscape and square source images use the same stable review-workspace footprint and are never cropped in the default fitted state.
- [x] When decision content exceeds the available height, only the decision panel scrolls and the image remains visible.
- [x] Previous, next, save and save-and-next controls remain reachable throughout review.
- [x] Fit, zoom in, zoom out and reset work without changing normalized overlay alignment.
- [x] Moving to another photo resets zoom, pan and decision-panel scroll position.
- [x] Overlay-to-control highlighting works for both reference faces and candidate detections and is usable by keyboard focus as well as pointer interaction.
- [x] The narrow-screen layout bounds the image height and does not allow the image to consume the full page before controls become reachable.
- [x] Existing correction persistence, automatic-miss behavior, completion arithmetic, metrics and exports remain unchanged.
- [x] Existing comparison images remain available across isolated catalogue switches when source filename and full SHA-256 match.
- [x] Automated coverage verifies view-state reset, overlay transforms, responsive layout and cross-catalogue comparison-photo retrieval.
- [x] The maintainer verified the merged workflow on 2026-08-05 and confirmed that it works as expected.

## Out of scope

- Changing detector matching, IoU rules, ground-truth semantics or gate arithmetic.
- Editing reference or candidate box geometry from the comparison page.
- Persisting zoom or pan state across photos or sessions.
- Changing the persisted correction-storage format.

## Completion evidence

- PR #79 implemented the viewport-fitted comparison workspace.
- PR #80 fixed comparison image retrieval across isolated catalogues.
- GitHub Actions build #495 passed the release build, full test suite, living-document validation, generated-document verification, review-application smoke verification and Windows PowerShell mixed-media verification.
- The maintainer tested the merged WI-0040 workflow on 2026-08-05 and confirmed that it works as expected.
