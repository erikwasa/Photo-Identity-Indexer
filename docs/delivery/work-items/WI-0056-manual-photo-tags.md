---
id: WI-0056
title: Add Immich-compatible hierarchical manual photo tags
milestone: M19
status_source: ../status/work-items.yaml
depends_on: [WI-0042]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0056: Add Immich-compatible hierarchical manual photo tags

## Objective

Provide maintainer-owned manual photo tags using Immich-compatible hierarchical tag-path semantics while keeping SQLite, not sidecars, as Photo Identity's canonical store.

PR #138 established flat canonical tags, revision-bound append-only add/remove history and photo-viewer controls. Before final verification, the product direction changed: automatic tagging is on hold and manual tags should preserve a hierarchy that can map cleanly to a future Immich export.

## Tag contract

- `/` is the hierarchy separator: `Places/Sweden/Stockholm` is a nested tag path.
- Each segment is case-insensitive and whitespace-normalized while stable display spelling is retained.
- Existing flat tags remain root tags.
- The canonical vocabulary exposes an internal id, leaf `name`, full `value`, immediate parent and optional color slot, matching Immich's logical tag shape closely enough for future ID mapping/export.
- Photo Identity IDs do not need to match Immich UUIDs.
- SQLite remains authoritative; no XMP/IPTC sidecars are created or modified.
- Tag assignment history remains auditable and tied to immutable asset revisions.
- Tag edits must not hydrate or modify original photos.

A literal slash inside one tag segment is no longer supported because slash now carries hierarchy semantics.

## Acceptance criteria

- [ ] Root and nested tags can be added, reloaded and removed from the photo viewer.
- [ ] `Places/Sweden/Stockholm` produces reusable parent vocabulary plus the assigned leaf path.
- [ ] Case and whitespace variants do not duplicate a canonical path.
- [ ] Existing flat tags continue to behave as root tags.
- [ ] `/api/tags` exposes id, leaf name, full value, parent and optional color fields suitable for later Immich export mapping.
- [ ] Removing an assignment retains canonical vocabulary and parent tags.
- [ ] Revision-bound add/remove audit history remains intact.
- [ ] Metadata-only tag operations do not hydrate or modify an original.
- [ ] Automated tests cover hierarchy parsing, parent creation/reuse, idempotence, remove/re-add and revision validation.

## Out of scope

Automatic tagging, sidecar write-back, tag rename/merge, bulk taxonomy management and smart collections (WI-0050).

## Verification

Run automated persistence/API tests, then verify root and nested tags in the local photo viewer, including an online-only original. After verification, complete WI-0056 and proceed to WI-0050.
