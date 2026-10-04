# Build context

Formal lifecycle status is resolved through `PhotoIdentity.Docs`; read the target shard and linked document before continuing.

## Current focus

WI-0165 / issue #499: maintained verification on 2026-10-04 confirmed slideshow counts and standalone **Prepare originals** state, but **Prepared** still does not appear after player-triggered preparation and is not restored after reloading `/slideshows`. Keep WI-0165 in review until both continuity failures are fixed and reverified.

WI-0178 / issue #481: PR #501 implements compact client-side slideshow-name filtering over the unified library. The search is trimmed/case-insensitive, preserves the existing deterministic alphabetical order, restores cached items without a reload, and keeps creation kind out of consumer navigation. Verify desktop and phone/PWA search reveal/focus, full/partial/mixed-case/whitespace queries, clear/Escape/no-results recovery and no horizontal overflow before completion.

WI-0182 / issue #490 remains ready for the technical photo-quality scoring evaluation. It is evaluation-first work and has not been implemented.

WI-0185 / issue #503 is ready under M35 to redesign identity audit around cross-person automatic assignments.

The maintainer accepted WI-0163, WI-0167, WI-0169, WI-0170, WI-0171, WI-0172, WI-0173, WI-0175, WI-0176 and WI-0177. On 2026-10-04 (Europe/Stockholm), the maintainer also accepted WI-0179, WI-0180, WI-0181, WI-0183 and WI-0184; issues #487, #488, #489, #491 and #492 are resolved. The remaining Prepared-state continuity defect belongs to WI-0165 / issue #499.

## Next concrete step

Verify WI-0178 after PR #501: on desktop and phone/PWA exercise search reveal/focus, full and partial names, mixed casing, surrounding whitespace, clear/Escape/no-results recovery, cached restoration and horizontal-overflow behavior. WI-0165 requires implementation for #499 before re-verification. WI-0182 and WI-0185 are ready when prioritized. M30 video support remains deferred.

## Relevant pointers

- docs/delivery/work-items/WI-0178-slideshow-library-filtering.md
- docs/delivery/work-items/WI-0165-slideshow-library-status-counts.md
- docs/delivery/work-items/WI-0182-photo-quality-scoring-evaluation.md
- docs/delivery/work-items/WI-0185-automatic-assignment-audit.md
