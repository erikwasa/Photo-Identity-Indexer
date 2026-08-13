---
id: M19
title: Photo metadata and semantic collections
status_source: ../status/milestones.yaml
depends_on: [M12, M14]
---

# M19: Photo metadata and semantic collections

## Outcome

The catalogue can organize photos using photographic capture metadata, location, people and maintainer-owned hierarchical tags. Saved smart collections combine these dimensions and reevaluate against the current catalogue so newly ingested matching photos appear automatically.

## Work items

- [WI-0056](../work-items/WI-0056-manual-photo-tags.md) — make manual photo tags hierarchical and compatible with Immich tag-path semantics while retaining SQLite as the source of truth
- [WI-0050](../work-items/WI-0050-exif-smart-collections.md) — ingest capture metadata and persist reusable smart collections over people, tags, location and taken-time filters

Automatic visible-content tagging is intentionally deferred. [WI-0049](../work-items/WI-0049-visible-content-tagging-experiment.md) remains as a design/experiment note that can be revived later, but it is not part of the active M19 completion path.

## Tag architecture

Manual tags are the supported tag source for M19. The logical tag shape follows Immich's hierarchical model: a tag has a local `name`, a full-path `value`, an optional parent and an optional color. `/` is the hierarchy separator, so values such as `Places/Sweden/Stockholm` represent nested tags rather than a literal slash inside one tag name.

Photo Identity continues to persist tags and revision-bound tag actions in SQLite. It does not adopt Immich's XMP sidecar write-back model. The compatibility goal is to keep tag hierarchy and export semantics close enough that a future exporter can map canonical tags and asset assignments into Immich without first flattening or reinterpreting the vocabulary.

Existing flat tags remain valid root tags. Tag assignment history remains auditable and bound to immutable asset revisions; originals remain read-only and tag edits must not hydrate an online-only original.

## Smart-collection semantics

A smart collection is a persisted query definition, not a copied list of asset IDs. Results are evaluated from the current catalogue each time the collection is opened or queried, so new matching photos join automatically.

The first production definition can combine all of these dimensions:

- people — one or more canonical people, with `all` or `any` matching inside the people dimension;
- tags — one or more canonical full tag values, with `all` or `any` matching inside the tag dimension;
- location — GPS-based geographic criteria when coordinates are available; the first implementation may use coordinate bounds without requiring reverse geocoding;
- taken time — inclusive photographic date ranges derived from capture metadata.

Different dimensions combine with AND semantics. For example, a collection can mean "Alice AND Bob, tagged `Trips/Italy`, inside a geographic area, taken from 2025-05-01 through 2025-05-10". Date input should support convenient forms such as `2016`, `2020-2021` and `2025/05/01-2025/05/10`, normalized to explicit inclusive date bounds for persistence/querying.

## Exit criteria

- Manual tags support hierarchical Immich-compatible tag values while remaining SQLite-backed and revision-audited.
- Capture time is stored with photographic local-time semantics instead of inventing UTC when the source metadata has no offset.
- GPS metadata is retained when available without making location mandatory.
- A maintainer can save, reopen, edit and delete smart-collection definitions.
- A saved smart collection reevaluates against the current catalogue and automatically includes newly ingested matching photos.
- Smart collections can combine people, hierarchical tags, location and taken-time criteria in one definition.
- Missing EXIF/GPS/tag/person data fails the relevant predicate rather than inventing metadata.
