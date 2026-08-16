---
id: WI-0061
title: Enrich Photo Details and preserve navigation context
milestone: M19
status_source: ../status/work-items.yaml
depends_on: [WI-0025, WI-0050]
related_adrs: []
affected_modules: [PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0061: Enrich Photo Details and preserve navigation context

## Objective

Make Photo Details show the privacy-safe catalogue information needed to understand the selected image and return the maintainer to the exact workspace state from which the photo was opened.

## Context

The current Photo Details route is revision-oriented and loads original availability plus manual tags. It does not expose the original file name or a consolidated list of confirmed people. Its return control is hard-coded to `/collections`, and the Smart Collections editor keeps its active selection, filters, result mode and paging only in component memory.

## In scope

- Add a revision-detail query/API contract that exposes the original **file name only**, derived from the catalogue source key without exposing the source root or source-relative directory path.
- Show canonical people known to be in the photo through confirmed evidence. Pending suggestions must not appear as confirmed people.
- Design the people response so WI-0062 can add photo-level manual presence evidence without changing the Photo Details contract again.
- Preserve Smart Collections navigation state when a result photo is opened and the user returns with browser Back or a mouse Back button.
- Preserve at least the selected saved collection or transient preview state, applied people/tags/location/taken filters, result mode and current result-page offset.
- Make photo links carry an explicit local return context rather than relying on a hard-coded destination.
- Replace the fixed `Back to collections` control with a context-aware label/destination such as `Back to smart collections` when the photo was opened from that workspace.
- Keep `/collections` as a safe fallback when no valid return context is supplied.
- Validate return destinations as local application routes so the mechanism cannot become an open redirect.
- Preserve the existing no-hydration behavior for ordinary Photo Details viewing.

## Recommended state model

- Saved Smart Collections should be reconstructable from a URL containing the saved collection identifier plus paging/result context.
- Transient unsaved previews may use tab-scoped browser session storage referenced by navigation context so large filter payloads do not need to be embedded in URLs.
- Browser history should contain enough information for normal Back navigation to restore the previous Smart Collections workspace rather than instantiate a blank editor.

## Out of scope

- Creating or editing manual photo-level people; that belongs to WI-0062.
- Changing face-identification evidence or suggestion semantics.
- Exposing full source paths, source roots or private directory structure to the browser.
- Changing the durable smart-collection definition model beyond what is required to restore navigation state.

## Acceptance criteria

- [ ] Photo Details shows the original file name without exposing its directory or source root.
- [ ] Photo Details shows canonical confirmed people known to be in the image and does not treat pending suggestions as confirmed people.
- [ ] Opening a photo from a saved Smart Collection and returning with browser/mouse Back restores the same saved collection and result page.
- [ ] Opening a photo from an unsaved Smart Collection preview and returning restores the applied preview filters and result context for that browser tab.
- [ ] Photo Details shows a context-aware Back destination when a valid source workspace is supplied and safely falls back when it is not.
- [ ] Return navigation cannot redirect outside the local Photo Identity application.
- [ ] The new detail/navigation operations do not hydrate an online-only original.
- [ ] Automated tests cover privacy-safe filename exposure, confirmed people, saved/transient navigation restoration and return-route validation.

## Verification requirements

Automated API/web tests plus a local browser pass using mouse/browser Back from both a saved Smart Collection and an unsaved preview are required.
