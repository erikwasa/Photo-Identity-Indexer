---
id: WI-0056
title: Add canonical photo tags and manual tagging
milestone: M19
status_source: ../status/work-items.yaml
depends_on: [WI-0042]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0056: Add canonical photo tags and manual tagging

## Objective

Establish the production tag representation for whole photos and let the maintainer add and remove tags manually from the photo viewer before automatic tagging is selected.

## Why

M19 needs tags to be useful even when no automatic model is installed or trusted. A human-owned tag vocabulary also gives the later visible-content experiment a stable integration target and a source of maintainer-labelled examples for measuring automatic tagging usefulness.

Manual assignments and model-produced evidence must remain distinguishable. Re-running an automatic model must never overwrite or silently remove a maintainer assignment.

## In scope

- Define canonical, case-insensitive tag identity with a human-readable display name.
- Bind tag assignments to immutable asset revisions so the assertion describes the exact photo content that was reviewed.
- Persist auditable manual add/remove actions with actor and timestamp provenance.
- Expose API contracts for listing, adding and removing tags on a photo revision.
- Add manual tag controls to the existing `/photo/{RevisionId}` viewer.
- Reuse existing canonical tags when the same spelling is entered with different casing or surrounding whitespace.
- Establish a clean boundary where future automatic evidence can reference the same canonical vocabulary without sharing or mutating manual assignment history.
- Keep original photos read-only and do not require image hydration merely to edit tag metadata.

## Initial semantics

- Tags are flat, free-form labels in this item. Hierarchies, synonyms and automatic place-name expansion are deferred.
- Canonical identity is normalized independently of display casing. The first accepted display spelling is retained until a separate rename capability is intentionally added.
- Manual tags are authoritative human assertions, not confidence-scored model evidence.
- Automatic tag evidence will remain separate from manual assignment history and can later contribute to effective/filterable tags only under an explicit policy.
- The automatic-evidence persistence schema is deliberately deferred to WI-0049. Model identity plus a scalar score is not sufficient provenance for all candidate approaches because preprocessing, prompts, vocabulary/tokenization or other pipeline inputs may affect the output.

## Out of scope

- Automatic image tagging inference or model installation.
- Selecting a production image-tagging model.
- Freezing the production automatic-evidence schema before WI-0049 establishes the complete reproducible inference-pipeline provenance and output shape.
- Smart-collection tag predicates; WI-0050 consumes this production representation.
- Hierarchical taxonomies, aliases/synonyms, tag merging or bulk tag maintenance.
- Writing EXIF/IPTC/XMP metadata back into original files.

## Acceptance criteria

- [ ] A maintainer can add a tag to a photo from the photo viewer and see it after reload.
- [ ] A maintainer can remove a manual tag without deleting the canonical tag vocabulary entry or unrelated future model evidence.
- [ ] Tag identity is case-insensitive and ignores surrounding/repeated whitespace while preserving a stable display name.
- [ ] Manual add/remove history is auditable and tied to the exact asset revision.
- [ ] Tag metadata operations do not modify or implicitly hydrate the original photo.
- [ ] Persistence and API contracts leave a clear boundary for future model evidence without conflating it with manual assignments.
- [ ] Automated tests cover normalization, idempotent add, remove/re-add, revision validation and migration behavior.

## Verification requirements

Automated persistence/API tests plus maintainer verification in the local photo viewer using non-sensitive tag names on representative archive photos.

## Planned slices

1. **Implemented** — persistence, migration and API contracts for canonical tags plus auditable manual assignments.
2. **Implemented** — photo-viewer controls plus hosted-route/Web-contract application coverage.
3. **Current** — CI/documentation validation and maintainer verification before handoff to WI-0049 and WI-0050.

## Completion notes

- Files changed: canonical tag value object; SQLite schema v13 and tag repository; tag API endpoints; Web tag contracts; photo-viewer tag controls/styles; integration/application coverage; M19 sequencing and delivery documentation.
- Trade-offs: tags remain flat and free-form; assignments are revision-bound; removal is an immediate audited action with re-add as the recovery path; automatic evidence storage is intentionally not frozen until WI-0049 establishes complete reproducible pipeline provenance.
- Deferred work: automatic evidence generation/schema; tag hierarchies/synonyms; tag rename/merge; bulk tag maintenance; tag predicates in smart collections.
- Commands run: GitHub Actions build/test/documentation/review/package/launcher verification through PR #138; maintainer photo-viewer verification remains required before completion.
