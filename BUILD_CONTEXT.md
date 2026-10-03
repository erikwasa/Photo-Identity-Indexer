# Build context

Formal lifecycle status is resolved through `PhotoIdentity.Docs`; read the target shard and linked document before continuing.

## Current focus

WI-0183 / issue #491: PR #496 simplifies `/smart-collections` with progressive disclosure. GPS, age and family-relationship detail controls are now conditional, normal Tags authoring is removed, and hidden legacy tag criteria remain carried through saved/query/navigation state with an explicit preservation notice. The implementation is in review; require CI plus maintainer desktop/phone verification and a representative save of an existing tagged Smart Collection before completion.

WI-0180 / issue #488: PR #495 is merged. The shared slideshow playback-preferences UI still requires explicit maintainer verification on desktop and phone/PWA in both `/slideshows` Playback preferences and the in-player Settings panel before WI-0180 can be completed.

WI-0181 / issue #489: PR #494 is merged. The Creative novelty checkbox/shared collection styling implementation still requires explicit maintainer desktop/phone verification of `/creative-collections` plus a `/smart-collections` regression check before WI-0181 can be completed.

WI-0176 / issue #461: PR #479 is merged and the maintainer confirms landscape panning is gone, but an Archive screenshot still shows the Local catalogue badge on a pale right-hand strip. Retest Archive landscape/portrait, header background, all destinations and Advanced after rebuilding. Keep #461 open and WI-0176 in review until maintained acceptance passes.

The maintainer accepted WI-0163, WI-0167, WI-0169, WI-0170, WI-0172 and WI-0173 on 2026-10-02 (Europe/Stockholm). Their documents record the acceptance, including the explicitly waived timing reports for WI-0169/WI-0170.

## Next concrete step

Verify WI-0183 after PR #496 CI: confirm the default Smart Collection editor is shorter, toggle GPS/age/family details on and off, check desktop/phone overflow, and save a representative pre-existing tagged collection to confirm its hidden tag criteria remain unchanged. WI-0180, WI-0181, WI-0176, WI-0165, WI-0175 and WI-0171 still retain their previously documented acceptance checks. M30 video support remains deferred.

## Relevant pointers

- docs/delivery/work-items/WI-0183-smart-collection-progressive-disclosure.md
- docs/delivery/work-items/WI-0180-playback-preferences-ui.md
- docs/delivery/work-items/WI-0181-creative-collection-checkbox-alignment.md
- docs/delivery/work-items/WI-0176-full-app-landscape-navigation.md
- docs/delivery/work-items/WI-0171-phone-slideshow-grid-artifacts.md
- docs/delivery/work-items/WI-0175-slideshow-catalogue-recovery.md
