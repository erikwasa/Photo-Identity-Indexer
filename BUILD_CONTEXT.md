# Build context

Formal lifecycle status is resolved through `PhotoIdentity.Docs`; read the target shard and linked document before continuing.

## Current focus

WI-0184 / issue #492: PR #497 adds one-action oldest-first/newest-first sorting to manual collections. Sorting resolves effective capture evidence for the whole collection in one set-oriented PostgreSQL query, keeps undated photos last in either direction, preserves prior relative order for equal timestamps, and persists the result as the existing explicit `RevisionIds` sequence. The implementation is in review; require CI plus maintainer desktop/phone verification before completion.

WI-0183 / issue #491: PR #496 is merged. The Smart Collection progressive-disclosure implementation still requires explicit desktop/phone verification and a representative save of an existing tagged Smart Collection to confirm its hidden tag criteria remain unchanged.

WI-0180 / issue #488: PR #495 is merged. The shared slideshow playback-preferences UI still requires explicit maintainer verification on desktop and phone/PWA in both `/slideshows` Playback preferences and the in-player Settings panel before WI-0180 can be completed.

WI-0181 / issue #489: PR #494 is merged. The Creative novelty checkbox/shared collection styling implementation still requires explicit maintainer desktop/phone verification of `/creative-collections` plus a `/smart-collections` regression check before WI-0181 can be completed.

WI-0176 / issue #461: PR #479 is merged and the maintainer confirms landscape panning is gone, but an Archive screenshot still shows the Local catalogue badge on a pale right-hand strip. Retest Archive landscape/portrait, header background, all destinations and Advanced after rebuilding. Keep #461 open and WI-0176 in review until maintained acceptance passes.

The maintainer accepted WI-0163, WI-0167, WI-0169, WI-0170, WI-0172 and WI-0173 on 2026-10-02 (Europe/Stockholm). Their documents record the acceptance, including the explicitly waived timing reports for WI-0169/WI-0170.

## Next concrete step

Verify WI-0184 after PR #497 CI: on desktop and phone/PWA exercise Oldest first and Newest first with dated, equal-date and undated photos; confirm undated photos remain last, equal-date order is stable, reload preserves the result and manual ↑/↓ adjustment still works. If practical, confirm a slideshow snapshot already running before a reorder is unchanged while a new session uses the saved order. WI-0183, WI-0180, WI-0181, WI-0176, WI-0165, WI-0175 and WI-0171 still retain their documented acceptance checks. M30 video support remains deferred.

## Relevant pointers

- docs/delivery/work-items/WI-0184-manual-collection-date-sort.md
- docs/delivery/work-items/WI-0183-smart-collection-progressive-disclosure.md
- docs/delivery/work-items/WI-0180-playback-preferences-ui.md
- docs/delivery/work-items/WI-0181-creative-collection-checkbox-alignment.md
- docs/delivery/work-items/WI-0176-full-app-landscape-navigation.md
- docs/delivery/work-items/WI-0171-phone-slideshow-grid-artifacts.md
- docs/delivery/work-items/WI-0175-slideshow-catalogue-recovery.md
