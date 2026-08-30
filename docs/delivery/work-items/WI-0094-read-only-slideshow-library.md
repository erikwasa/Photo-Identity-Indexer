---
id: WI-0094
title: Add a read-only slideshow library with standalone original preparation
milestone: M22
status_source: ../status/work-items.yaml
depends_on: [WI-0050, WI-0083, WI-0092, WI-0093]
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, documentation]
---

# WI-0094: Add a read-only slideshow library with standalone original preparation

## Objective

Add a simple phone/basic-user surface for consuming saved Smart Collections without exposing collection editing.

The page should let a user:

- see the saved Smart Collections;
- start a slideshow;
- change global slideshow settings;
- prepare a collection's originals without entering fullscreen or starting playback; and
- see preparation progress/result.

It must not expose Smart Collection definition editing or photo/catalogue mutation controls.

## Product boundary

This is a **read-only UI surface**, not an authorization boundary.

Photo Identity remains unauthenticated on its trusted-LAN deployment path. A user who deliberately navigates to existing operator/editor routes can still reach them unless a future authentication/role milestone changes that trust model.

Therefore the new surface should contain no edit/delete/filter-definition/photo-metadata controls and should avoid general operator navigation in its basic-user layout.

## Route and layout

Add a dedicated route, recommended as:

    /slideshows

Use a minimal consumer-oriented layout rather than ordinary operator workspace navigation.

Display saved Smart Collections as simple cards/rows, primarily by name. Each collection should offer:

- **Start slideshow**
- **Prepare originals**

The page should expose the same global slideshow settings used inside parent slideshow controls. Prefer one reusable settings component/model rather than duplicating settings behavior.

Starting a slideshow here should return to /slideshows after deliberate Exit.

## Standalone Prepare originals

Prepare originals must not enter fullscreen or start autoplay.

The action should:

1. create one immutable saved-collection snapshot;
2. run the WI-0093 full-snapshot preflight/progress flow on the server;
3. hydrate/verify required originals with existing bounded concurrency and ownership rules;
4. show aggregate progress on the read-only page;
5. on success, release preparation-specific temporary protection while leaving Photo-Identity-owned prepared files local and eligible for normal managed LRU.

This is preparation for future viewing, not a permanent offline-album pin.

If collection membership changes after standalone preparation, slideshow start remains authoritative for its new snapshot and prepares any newly required items when Prepare originals is enabled.

## Interaction and lifecycle

- Starting preparation does not require fullscreen.
- Server-side preparation should continue while client polling is temporarily paused, subject to normal process lifetime.
- Multiple accidental taps must not start duplicate work for the same collection/snapshot.
- A parent can cancel active preparation.
- Capacity failure and no-progress recovery reuse WI-0093.

## Acceptance criteria

- [ ] A dedicated /slideshows page lists saved Smart Collections without collection edit/delete controls.
- [ ] The page exposes no photo metadata/tag/people/Places mutation controls.
- [ ] It uses a minimal consumer layout without ordinary operator navigation.
- [ ] Documentation states this is UI simplification, not authentication/authorization.
- [ ] Start slideshow works and deliberate Exit returns to the slideshow library.
- [ ] Global slideshow settings can be changed here and are the same persisted settings used in playback.
- [ ] Manual navigation and Orientation from WI-0092 are available here.
- [ ] Prepare originals can be started without fullscreen or slideshow playback.
- [ ] Standalone preparation uses an immutable snapshot and existing bounded storage/ownership rules.
- [ ] Standalone preparation shows useful progress/no-progress recovery.
- [ ] Successful preparation removes temporary protection but does not immediately release app-owned originals.
- [ ] Starting a later slideshow reuses already-local originals.
- [ ] Duplicate preparation requests are coalesced/rejected safely.
- [ ] Tests cover the read-only route, absence of mutation actions, return navigation, settings reuse and standalone preparation lifecycle.
- [ ] Real-phone verification confirms the page is usable as the normal content-consumption entry point.

## Non-goals

- Accounts, roles or authentication.
- A security boundary between basic and operator users.
- Editing Smart Collections from the slideshow library.
- Permanent per-collection offline pinning.
