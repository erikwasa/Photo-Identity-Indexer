---
id: WI-0046
title: Simplify the application around Review and Library
milestone: M18
status_source: ../status/work-items.yaml
depends_on: [WI-0025, WI-0045, WI-0048]
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Api]
---

# WI-0046: Simplify the application around Review and Library

## Objective

Reorganize the application so the primary navigation reflects the two everyday use cases: handling new images/identity review and browsing or creating photo collections.

## Why

Detector evaluation, comparison, rollout, audit and diagnostic pages were valuable while building the system but now compete visually with the normal operator workflow.

## In scope

- Make Review the primary workflow for new-photo/archive status, face review, people creation, suggestion regeneration and review completion.
- Make Library the primary workflow for browsing/filtering collections and photos.
- Add a settings/advanced entry point for configuration, person maintenance, audit, progress and model/detector engineering tools.
- Preserve advanced capabilities and stable deep links where practical; use redirects when routes move.
- Keep actionable new-photo/archive state easy to reach from Review even though archive configuration moves to Settings.
- Update responsive navigation for Windows desktop and narrow/mobile browser use.

## Out of scope

- Removing evaluation, comparison or rollout functionality.
- Reimplementing collection query semantics.
- Changing the catalogue path.

## Acceptance criteria

- [ ] Primary navigation clearly emphasizes Review and Library plus a Settings/advanced entry point.
- [ ] The normal new-image loop can be completed without visiting engineering pages.
- [ ] Collection browsing remains directly reachable as Library.
- [ ] Detector/model engineering, audit, progress and advanced person maintenance remain accessible outside the primary navigation.
- [ ] Existing useful URLs are preserved or redirected.
- [ ] Desktop and narrow/mobile navigation is usable without crowding.

## Verification requirements

Human workflow verification on Windows and the trusted mobile-browser path, plus component/routing tests where practical.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
