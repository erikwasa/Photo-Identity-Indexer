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

Establish the production tag representation for whole photos and provide manual add/remove controls as a fallback and correction path for cases where automatic tagging is unavailable, misses a useful concept or needs human intervention.

## Why

Automatic tagging is the intended primary tagging path for M19, but it needs a safe recovery mechanism. A maintainer must be able to fill gaps manually without changing the original photo, and those human interventions must remain distinguishable from model-produced evidence. The same canonical tag vocabulary also gives the visible-content experiment a stable integration target and can provide maintainer-labelled examples for measuring automatic tagging usefulness.

Implementing the manual path first is an architectural sequencing choice, not a statement that manual tagging is the product baseline or that maintainers are expected to tag the archive by hand.

## In scope

- Define canonical, case-insensitive tag identity with a human-readable display name.
- Bind manual tag assignments to immutable asset revisions so the assertion describes the exact photo content that was reviewed.
- Persist auditable manual add/remove actions with actor and timestamp provenance.
- Expose API contracts for listing, adding and removing manual tags on a photo revision.
- Add manual fallback/correction controls to the existing `/photo/{RevisionId}` viewer.
- Reuse existing canonical tags when the same spelling is entered with different casing or surrounding whitespace.
- Establish a clean boundary where future automatic evidence can reference the same canonical vocabulary without sharing or mutating manual assignment history.
- Keep original photos read-only and do not require image hydration merely to edit tag metadata.

## Initial semantics

- Tags are flat, free-form labels in this item. Hierarchies, synonyms and automatic place-name expansion are deferred.
- Canonical identity is normalized independently of display casing. The first accepted display spelling is retained until a separate rename capability is intentionally added.
- Automatic tagging is expected to provide the normal/default tag set once the production automatic pipeline exists.
- Manual tags are explicit human fallback/correction assertions, not confidence-scored model evidence.
- Manual assignment history and automatic evidence remain separate. A future effective-tag policy must define how an explicit maintainer correction, including suppression of an incorrect automatic tag, takes precedence without deleting the underlying model evidence.
- This item implements manual positive assignment/removal only. Model-only suppression semantics belong with the production automatic-tag integration because no automatic evidence exists yet to suppress.
- The automatic-evidence persistence schema is deliberately deferred to WI-0049. Model identity plus a scalar score is not sufficient provenance for all candidate approaches because preprocessing, prompts, vocabulary/tokenization or other pipeline inputs may affect the output.

## Out of scope

- Automatic image tagging inference or model installation.
- Selecting a production image-tagging model.
- Freezing the production automatic-evidence schema before WI-0049 establishes the complete reproducible inference-pipeline provenance and output shape.
- Defining final effective-tag precedence/suppression behavior before automatic evidence exists.
- Smart-collection tag predicates; WI-0050 consumes the canonical representation.
- Hierarchical taxonomies, aliases/synonyms, tag merging or bulk tag maintenance.
- Writing EXIF/IPTC/XMP metadata back into original files.

## Acceptance criteria

- [ ] A maintainer can add a fallback/correction tag to a photo from the photo viewer and see it after reload.
- [ ] A maintainer can remove a manual tag without deleting the canonical tag vocabulary entry or unrelated future model evidence.
- [ ] Tag identity is case-insensitive and ignores surrounding/repeated whitespace while preserving a stable display name.
- [ ] Manual add/remove history is auditable and tied to the exact asset revision.
- [ ] Tag metadata operations do not modify or implicitly hydrate the original photo.
- [ ] Persistence and API contracts leave a clear boundary for future automatic evidence without conflating it with manual interventions.
- [ ] Automated tests cover normalization, idempotent add, remove/re-add, revision validation and migration behavior.

## Verification requirements

Automated persistence/API tests plus maintainer verification in the local photo viewer using non-sensitive tag names on representative archive photos. Verification confirms that the fallback controls work without implying a manual-tagging workflow for the full archive.

## Planned slices

1. **Implemented** — persistence, migration and API contracts for canonical tags plus auditable manual assignments.
2. **Implemented** — photo-viewer fallback/correction controls plus hosted-route/Web-contract application coverage.
3. **Current** — CI/documentation validation and maintainer verification before handoff to WI-0049 and WI-0050.

## Completion notes

- Files changed: canonical tag value object; SQLite schema v13 and tag repository; tag API endpoints; Web tag contracts; photo-viewer tag controls/styles; integration/application coverage; M19 sequencing and delivery documentation.
- Trade-offs: tags remain flat and free-form; manual assignments are revision-bound; removal is an immediate audited action with re-add as the recovery path; automatic evidence storage and model-only suppression semantics are intentionally not frozen until the automatic pipeline is established.
- Deferred work: automatic evidence generation/schema; effective automatic/manual precedence and model-only suppression; tag hierarchies/synonyms; tag rename/merge; bulk tag maintenance; tag predicates in smart collections.
- Commands run: GitHub Actions build/test/documentation/review/package/launcher verification through PR #138; maintainer photo-viewer verification remains required before completion.
