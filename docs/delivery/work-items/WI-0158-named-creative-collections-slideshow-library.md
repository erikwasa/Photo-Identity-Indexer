---
id: WI-0158
title: Make named Creative Collections first-class slideshow library entries
milestone: M26
status_source: ../status/work-items.yaml
depends_on: [WI-0121]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Integration.Tests, docs]
---

# WI-0158: Make named Creative Collections first-class slideshow library entries

## Objective

Make a Creative Collection a durable, named object that can coexist with other Creative Collections derived from the same Smart Collection and can be found and launched directly from the Slideshows page.

## Why

WI-0121 productized Creative Collection recipes, but its first persisted shape deliberately stores one recipe per anchor Smart Collection. That makes the Creative result behave like a mode of the Smart Collection rather than a first-class slideshow source.

The desired everyday workflow is different: one Smart Collection should be able to seed several distinct Creative Collections with different target counts or context policies, each with a meaningful name. Those Creative Collections should then be discoverable from the read-only Slideshows page without returning to Smart Collection editing.

## In scope

- Give every persisted Creative Collection a stable identity independent of its anchor Smart Collection.
- Add a required, user-editable Creative Collection name.
- Replace the one-recipe-per-anchor persistence constraint with a many-to-one relationship so one Smart Collection can own zero, one or many Creative Collections.
- Preserve each Creative Collection's own target count, context policy and versioned recipe fields independently.
- Let the Smart Collection workflow create a new Creative Collection instead of only overwriting the anchor's single recipe.
- Support loading, renaming, editing and deleting one Creative Collection without changing sibling Creative Collections or the anchor Smart Collection.
- Migrate existing singleton Creative recipes into the new identity/name model without losing their recipe settings. Use a deterministic operator-friendly fallback name derived from the anchor Smart Collection when no explicit Creative name exists.
- Extend the read-only `/slideshows` library to list named Creative Collections alongside ordinary saved Smart Collections and identify the anchor Smart Collection for context.
- Allow a named Creative Collection to start the existing Creative materialization/snapshot/playback path directly from the Slideshows page.
- Preserve the existing immutable slideshow snapshot boundary: launching a Creative Collection materializes its recipe and then hands a fixed revision sequence to playback.

## Out of scope

- Replacing or weakening exact Smart Collection semantics.
- Manually adding/removing individual photos from a Creative Collection; manual photo-list slideshow curation remains WI-0145/WI-0146.
- Folder/hierarchy organization for Creative Collections.
- Automatically generating multiple Creative variants without an explicit user save/create action.
- Changing the Creative selection heuristics themselves.

## Acceptance criteria

- [x] A persisted Creative Collection has a stable ID, a non-empty editable name and an anchor Smart Collection ID.
- [x] Two or more Creative Collections can be created from the same Smart Collection with independent names and recipe settings.
- [x] Editing, renaming or deleting one Creative Collection does not modify sibling Creative Collections or the anchor Smart Collection.
- [x] Existing WI-0121 singleton recipes migrate without losing target/context/policy settings and remain usable after the schema change.
- [x] The Slideshows page lists named Creative Collections as first-class launchable entries alongside ordinary Smart Collections.
- [x] A Creative Collection entry on the Slideshows page makes its source Smart Collection understandable without exposing recipe implementation details.
- [x] Starting a Creative Collection from the Slideshows page uses its current recipe to materialize the same immutable Creative slideshow snapshot used by the existing Smart Collection Creative flow.
- [x] Automated tests cover many-to-one persistence, migration, create/rename/edit/delete behavior, independent sibling settings, slideshow-library discovery and direct Creative launch.

## Verification requirements

Automated persistence/API/UI coverage plus maintainer verification with one Smart Collection that has at least two named Creative Collections with visibly different recipe settings. Verify both appear independently on the Slideshows page, can each be launched, and remain independent after renaming/editing one of them.

## Design notes

- The durable identity belongs to the Creative Collection, not to a particular materialized slideshow snapshot. Regeneration may change selected revisions while the Creative Collection ID and name remain stable.
- The anchor Smart Collection remains the exact query source and should continue to own its own lifecycle. Deleting an anchor cascades its dependent Creative Collections through the existing relationship.
- Naming is presentation metadata for the Creative Collection; it does not become a Smart Collection filter or canonical photo metadata.
- The Slideshows page remains a read-only consumption surface. Creation/editing lives on a focused `/creative-collections` operator page where the source Smart Collection is explicit; the Slideshows page links to that manager but does not mutate recipes itself.

## Implementation notes

- `CreativeCollectionId` and validated `CreativeCollectionName` make recipe identity independent from the anchor Smart Collection while retaining the complete versioned WI-0121 recipe settings.
- PostgreSQL and SQLite recipe repositories expose list/create/update/delete by Creative Collection ID plus compatibility methods for the original one-recipe-per-anchor callers.
- The first named-recipe access performs an idempotent adapter-local shape upgrade when the legacy singleton table is detected. Existing rows keep their recipe settings, use the anchor ID as the deterministic migrated Creative Collection ID and receive `<Smart Collection name> Creative` as the fallback display name.
- First-class API routes live under `/api/creative-collections`; anchor-scoped creation/listing lives under `/api/smart-collections/{id}/creative-collections`. Original `/creative-recipe` routes remain as compatibility surfaces.
- `/creative-collections` provides explicit source-Smart-Collection selection and independent create/rename/edit/delete controls. `/slideshows` renders named Creative cards with `Creative · from <anchor>` context and a direct management link.
- Direct launch keeps the existing `/slideshow/{id}?creative=true` player contract. Named Creative IDs are resolved back to their anchor only for materialization and exposure accounting, while the immutable snapshot carries the Creative Collection ID/name and fixed revision sequence.
- Focused tests cover legacy migration, multiple sibling recipes, rename/delete independence, named snapshot identity and Creative library navigation.
- Implementation PR: #414. Completion remains pending CI and maintainer desktop/phone verification.
