---
id: M18
title: Operator application experience
status_source: ../status/milestones.yaml
depends_on: [M12, M14, M17]
---

# M18: Operator application experience

## Outcome

Normal use of Photo Identity is centered on two clear activities — reviewing new photos and identities, and browsing the photo library — while configuration and engineering tools move out of the primary navigation and Windows startup no longer requires remembering a sequence of scripts or commands.

## Work items

- [WI-0046](../work-items/WI-0046-simplified-application-shell.md) — simplify the primary navigation around Review and Library
- [WI-0048](../work-items/WI-0048-configuration-page.md) — consolidate archive coverage and matching policy into a settings page
- [WI-0051](../work-items/WI-0051-one-click-windows-launcher.md) — provide a double-clickable Windows launcher for the existing published application
- [WI-0052](../work-items/WI-0052-packaged-windows-application.md) — package the application so routine use no longer depends on manual publish or environment-variable setup
- [WI-0055](../work-items/WI-0055-packaged-runtime-regressions.md) — restore packaged review/archive behavior and expose bounded-hydration policy
- [WI-0058](../work-items/WI-0058-face-details-image-quality.md) — persist high-quality face-review derivatives from full-resolution source pixels so review quality is independent of original hydration state
- [WI-0059](../work-items/WI-0059-full-photo-from-face-review.md) — open the containing full photo from Face Details without losing review context
- [WI-0060](../work-items/WI-0060-streamline-bulk-face-review.md) — add range selection and persistent one-step bulk review actions

## Scope boundary

Changing the catalogue database path from inside the running application is explicitly deferred. The initial settings work manages runtime/application policy and archive coverage only.

## Exit criteria

- Review and Library are the dominant primary navigation destinations.
- Evaluation, comparison, rollout, audit and advanced maintenance remain available without occupying the primary workflow.
- Supported settings can be changed from the application without editing commands.
- A normal Windows user can start the application from one file or packaged executable.
- Face review provides durable high-quality image context for identity decisions without depending on whether the authoritative original is currently local.
- Bulk face review supports efficient range-based selection and actions from the operator's current scroll position.
