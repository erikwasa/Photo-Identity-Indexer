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

## Scope boundary

Changing the catalogue database path from inside the running application is explicitly deferred. The initial settings work manages runtime/application policy and archive coverage only.

## Exit criteria

- Review and Library are the dominant primary navigation destinations.
- Evaluation, comparison, rollout, audit and advanced maintenance remain available without occupying the primary workflow.
- Supported settings can be changed from the application without editing commands.
- A normal Windows user can start the application from one file or packaged executable.
