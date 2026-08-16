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

Make Photo Details show the privacy-safe catalogue information needed to understand the selected image, use the best already-local image source in the viewer, and return the maintainer to the exact workspace state from which the photo was opened.

## Context

The current Photo Details route is revision-oriented and loads original availability plus manual tags. It does not expose the original file name or a consolidated list of confirmed people. Its return control is hard-coded to `/collections`, and the Smart Collections editor keeps its active selection, filters, result mode and paging only in component memory.

The current viewer also prefers the durable review proxy even when the revision-verified authoritative original is already local, and the explicit `Open original` control navigates the browser away from the current Photo Details view. Original availability is presented twice: once beside the section heading and again as a `State` row, while Photo Identity managed hydration and OneDrive/user pinning are shown as separate rows.

## In scope

- Add a revision-detail query/API contract that exposes the original **file name only**, derived from the catalogue source key without exposing the source root or source-relative directory path.
- Show canonical people known to be in the photo through confirmed evidence. Pending suggestions must not appear as confirmed people.
- Design the people response so WI-0062 can add photo-level manual presence evidence without changing the Photo Details contract again.
- Make Photo Details prefer the authoritative original whenever it is already local and revision-verified; a durable review proxy is a fallback for an original that is not currently safe to open locally.
- Keep ordinary viewer access no-hydration: checking/viewing a photo must never download an online-only original implicitly.
- After an explicit `Load original` operation reaches the ready state, switch the image shown in the existing Photo Details view to the authoritative original without downloading it as an attachment or opening a new browser tab.
- Remove the redundant new-tab/download-style `Open original` navigation once the local original is the image displayed by the viewer.
- Simplify authoritative-original status so the state appears once as a badge and management is shown as one `Managed by` row:
  - `Photo Identity` when the current hydration is managed by Photo Identity;
  - `OneDrive` when it is user/OneDrive pinned but not Photo Identity managed; and
  - `No` otherwise.
- Preserve Smart Collections navigation state when a result photo is opened and the user returns with browser Back or a mouse Back button.
- Preserve at least the selected saved collection or transient preview state, applied people/tags/location/taken filters, result mode and current result-page offset.
- Make photo links carry an explicit local return context rather than relying on a hard-coded destination.
- Replace the fixed `Back to collections` control with a context-aware label/destination such as `Back to smart collections` when the photo was opened from that workspace.
- Keep `/collections` as a safe fallback when no valid return context is supplied.
- Validate return destinations as local application routes so the mechanism cannot become an open redirect.

## Implementation slices

1. **Photo viewer/original-state cleanup** — prefer an already-local verified original over proxy, keep proxy fallback/no-hydration semantics, switch to the original in-place after explicit hydration, and simplify the state/management UI.
2. **Photo detail metadata** — expose original file name plus consolidated confirmed people without leaking private source paths.
3. **Navigation restoration** — preserve saved/transient Smart Collection state and replace the fixed Back destination with validated return context.

## Recommended state model

- Saved Smart Collections should be reconstructable from a URL containing the saved collection identifier plus paging/result context.
- Transient unsaved previews may use tab-scoped browser session storage referenced by navigation context so large filter payloads do not need to be embedded in URLs.
- Browser history should contain enough information for normal Back navigation to restore the previous Smart Collections workspace rather than instantiate a blank editor.

## Out of scope

- Creating or editing manual photo-level people; that belongs to WI-0062.
- Changing face-identification evidence or suggestion semantics.
- Exposing full source paths, source roots or private directory structure to the browser.
- Changing the durable smart-collection definition model beyond what is required to restore navigation state.
- Changing collection/gallery thumbnail policy; original-first behavior in this work item is specific to the Photo Details viewer.

## Acceptance criteria

- [ ] Photo Details shows the original file name without exposing its directory or source root.
- [ ] Photo Details shows canonical confirmed people known to be in the image and does not treat pending suggestions as confirmed people.
- [ ] When a revision-verified original is already local, Photo Details displays that original rather than an existing review proxy.
- [ ] When the original is online-only, normal Photo Details viewing can use an existing review proxy without requesting hydration.
- [ ] After an explicit `Load original` completes, the original is displayed in the same Photo Details web view without a download or new-tab navigation.
- [ ] Original state appears once as a badge and the UI shows one `Managed by: Photo Identity|OneDrive|No` value rather than separate management/pinning rows.
- [ ] Opening a photo from a saved Smart Collection and returning with browser/mouse Back restores the same saved collection and result page.
- [ ] Opening a photo from an unsaved Smart Collection preview and returning restores the applied preview filters and result context for that browser tab.
- [ ] Photo Details shows a context-aware Back destination when a valid source workspace is supplied and safely falls back when it is not.
- [ ] Return navigation cannot redirect outside the local Photo Identity application.
- [ ] The new detail/navigation/viewer operations do not hydrate an online-only original implicitly.
- [ ] Automated tests cover original-first/proxy-fallback viewer behavior, privacy-safe filename exposure, confirmed people, saved/transient navigation restoration and return-route validation.

## Verification requirements

Automated API/web tests plus a local browser pass are required. The local pass must verify original-first display for an already-local photo, proxy fallback for an online-only photo, in-place transition after `Load original`, the simplified state/management presentation, and mouse/browser Back from both a saved Smart Collection and an unsaved preview.
