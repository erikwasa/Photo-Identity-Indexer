---
id: WI-0146
title: Add manual slideshow curation and launch UI
milestone: M28
status_source: ../status/work-items.yaml
depends_on: [WI-0145]
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0146: Add manual slideshow curation and launch UI

## Objective

Let the user build a slideshow by selecting individual photos and launch it from the existing slideshow experience.

## Why

The explicit collection model is useful only when adding/removing photos is lightweight enough for ordinary curation.

## In scope

- Create/choose an explicit collection from normal photo/library surfaces.
- Add and remove individual photos without an operator-heavy editor.
- Provide a simple curation view with deterministic default ordering.
- Show explicit collections in the slideshow library and launch normal playback.
- Add custom reorder only if it remains simple.

## Out of scope

- A complex playlist editor.
- Creative Collection automation.
- Public sharing/export.

## Acceptance criteria

- [ ] A user can create a named manual collection and add photos during browsing.
- [ ] Membership can be reviewed and removed independently of Smart Collection filters.
- [ ] It appears in the slideshow library and launches normally.
- [ ] Phone interaction remains usable.
- [ ] Primary create/add/remove/launch flow is tested.

## Verification requirements

Automated integration tests plus maintainer desktop/phone curation and playback verification.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
