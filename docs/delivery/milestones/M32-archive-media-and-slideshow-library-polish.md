---
id: M32
title: Archive media compatibility and slideshow library polish
status_source: ../status/milestones.yaml
depends_on: [M12, M26, M27, M28]
---

# M32: Archive media compatibility and slideshow library polish

## Outcome

Photo Identity handles the real DNG files now present in the maintained archive and makes the slideshow/Creative Collection experience easier to understand and more reliable on desktop and phone without regressing established slideshow performance.

This milestone remains follow-up work rather than a slideshow redesign. It does not reopen generic RAW support for camera formats that are not present. The additional follow-ups record real maintainer findings around Prepared-state continuity, OneDrive-unavailable recovery, standalone-PWA navigation, archive-scale Creative materialization, navigation of a growing slideshow library, and collection-authoring usability.

## Work items

- [WI-0164](../work-items/WI-0164-dng-archive-support.md) - add verified end-to-end DNG support using representative private archive samples.
- [WI-0165](../work-items/WI-0165-slideshow-library-status-counts.md) - show compact preparation state and truthful photo-count information in the slideshow library, including continuity after player-triggered preparation.
- [WI-0167](../work-items/WI-0167-onedrive-slideshow-availability.md) - fail best-quality preparation actionably when online-only originals are needed but the OneDrive sync client is unavailable.
- [WI-0168](../work-items/WI-0168-pwa-slideshow-navigation.md) - add bidirectional navigation between the normal installed PWA and the simplified slideshow surface.
- [WI-0169](../work-items/WI-0169-creative-collection-scale.md) - bound large/Broad Creative Collection materialization so realistic archive-scale previews do not time out.
- [WI-0173](../work-items/WI-0173-slideshow-library-navigation-cache.md) - reuse library cards and covers across navigation.
- [WI-0171](../work-items/WI-0171-phone-slideshow-grid-artifacts.md) - diagnose and resolve thin grid-like phone rendering artifacts.
- [WI-0172](../work-items/WI-0172-slideshow-collection-management.md) - provide a separate full-app collection-management hub.
- [WI-0177](../work-items/WI-0177-unified-slideshow-library.md) - present Smart, manual and Creative slideshows as one consumer-facing library without creation-type grouping.
- [WI-0178](../work-items/WI-0178-slideshow-library-filtering.md) - add compact name filtering and deterministic sorting over the unified slideshow library.
- [WI-0179](../work-items/WI-0179-smart-collection-orientation-filter.md) - add visually correct portrait/landscape filtering to Smart Collections with existing-catalogue geometry fallback.
- [WI-0180](../work-items/WI-0180-playback-preferences-ui.md) - improve playback-preference checkbox alignment, touch targets and compact duration layout.
- [WI-0181](../work-items/WI-0181-creative-collection-checkbox-alignment.md) - fix Creative Collection checkbox alignment and make shared collection-form styling explicit.
- [WI-0182](../work-items/WI-0182-photo-quality-scoring-evaluation.md) - evaluate inspectable technical photo-quality evidence before deciding whether it should influence selection.
- [WI-0183](../work-items/WI-0183-smart-collection-progressive-disclosure.md) - shorten Smart Collection authoring with progressive disclosure and remove unused Tags authoring without losing legacy criteria.
- [WI-0184](../work-items/WI-0184-manual-collection-date-sort.md) - add one-action oldest/newest chronological sorting for manual collections.

## Delivery principles

- Implement RAW support from real archive evidence rather than guessing generic camera-format behavior.
- Keep originals immutable; browser viewing uses application-owned rendered derivatives where needed.
- Preserve the cheap initial slideshow-library path established by WI-0108.
- Do not describe an unprepared slideshow as unplayable.
- Present exact counts only when the underlying collection semantics make them exact.
- Keep gallery additions visually small and usable on desktop and phone.
- Do not require OneDrive merely to play already-local originals; when hydration is required and the sync client is unavailable, fail with an actionable recovery path rather than waiting indefinitely.
- Keep the slideshow consumer surface simple while ensuring standalone-PWA users can navigate to it and back to the full app.
- Treat Smart, manual and Creative as authoring/implementation distinctions rather than primary consumer-library navigation categories.
- Keep large-library navigation visually quiet: prefer lightweight presentation-layer search/sort over a management-style filter surface.
- Treat expensive Creative derived evidence as bounded/versioned work suitable for reuse; do not solve archive-scale timeouts only by increasing client timeouts.
- Keep collection authoring compact through progressive disclosure and small, explicit controls rather than new management-heavy surfaces.
- Keep derived orientation/quality evidence separate from canonical archive truth and do not hydrate originals solely for presentation classification when trustworthy derivatives suffice.

## Exit criteria

- [x] Existing and newly discovered DNG files can enter the normal archive pipeline and be viewed/processed through supported derivatives.
- [x] Representative private DNG samples have accepted orientation, colour, metadata, runtime and memory evidence.
- [ ] Slideshow cards show preparation state only when it is valid for the current revision set, including after successful best-quality preparation performed during normal slideshow playback.
- [ ] Manual and Smart slideshow counts are truthful, and Creative Collection quantity wording distinguishes target/maximum from exact membership when necessary.
- [ ] Slideshow-library card rendering remains responsive and does not require full snapshots solely for decorative status/count information.
- [ ] Best-quality preparation reports an actionable OneDrive-unavailable failure when an online-only original cannot be hydrated because the sync client is unavailable, without affecting already-local playback.
- [ ] Installed-PWA navigation provides a discoverable route from the full app to Slideshows and back without relying on browser chrome.
- [ ] A representative approximately 1,000-photo anchor with target 150 and Broad Creative context completes within the maintained client/server request boundary or reports a bounded actionable failure, with stable expensive derived evidence reused where appropriate.
- [x] Smart, manual and Creative slideshows appear in one continuous consumer library without creation-type sections while preserving correct playback and preparation behavior.
- [ ] A populated unified library can be narrowed quickly by slideshow name using a compact filter/search control on desktop and phone/PWA, with deterministic ordering and an accessible no-results recovery path.
- [ ] Smart Collection authoring supports portrait/landscape filtering with correct existing-catalogue behavior and presents optional filters progressively rather than keeping inactive detail fields visible.
- [ ] Playback/Creative authoring checkboxes and duration controls are clearly aligned, associated and phone-friendly.
- [ ] Manual collections can be reordered oldest/newest in one action without losing explicit manual ordering semantics.
- [ ] Technical photo-quality scoring has representative evaluation evidence and a documented adoption/no-adoption decision before it changes selection behavior.
- [ ] Maintainer verifies the remaining slideshow/PWA behavior on desktop and phone and the Creative scale case on representative private catalogue data.

## Maintainer verification update — 2026-10-04

Maintained verification accepted WI-0171 (phone slideshow grid artifacts), WI-0175 (slideshow catalogue/reload reliability) and WI-0177 (unified slideshow library). WI-0178 is therefore unblocked and ready for implementation.

WI-0165 remains open: counts are correct and standalone **Prepare originals** shows **Prepared**, but player-triggered preparation still does not establish the library Prepared state and a page reload does not restore a previously visible Prepared state. Issue #499 tracks those remaining acceptance failures. M32 remains in progress.
