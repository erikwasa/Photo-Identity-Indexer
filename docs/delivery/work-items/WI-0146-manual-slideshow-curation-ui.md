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

- [x] A user can create a named manual collection and add photos during browsing.
- [x] Membership can be reviewed and removed independently of Smart Collection filters.
- [x] It appears in the slideshow library and launches normally.
- [ ] Phone interaction remains usable.
- [x] Primary create/add/remove/launch flow is tested.

## Verification requirements

Automated integration tests plus maintainer desktop/phone curation and playback verification.

## Completion notes

- Files changed: Photo Details manual-slideshow picker, manual collection review/reorder page, slideshow library integration, manual playback routing, library cover handling, Web contracts, focused integration tests, and delivery tracking.
- Trade-offs: curation remains intentionally lightweight. New collections start with the current Photo Details revision, adding from other photos appends deterministically, and the review page offers remove plus one-step move up/down rather than a complex playlist editor. Manual collections reuse the existing slideshow snapshot/playback/original-preparation pipeline but skip Smart Collection exposure writes because their IDs live in a separate aggregate.
- Deferred work: maintainer desktop/phone verification is intentionally deferred so WI-0145 and WI-0146 can be reviewed together end-to-end after this PR merges. The phone acceptance checkbox remains open until that pass.
- Commands run: implementation prepared through the GitHub connector; PR CI supplies build, integration, package and docs validation.
