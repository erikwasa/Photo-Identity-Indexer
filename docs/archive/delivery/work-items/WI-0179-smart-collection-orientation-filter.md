---
id: WI-0179
title: Add photo orientation filtering to Smart Collections
milestone: M32
status_source: PhotoIdentity.Docs
depends_on: []
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0179: Add photo orientation filtering to Smart Collections

## Context

Issue #487 requests a simple way to build collections around visually landscape or portrait photographs. Smart Collections are the correct ownership boundary because Creative Collections already use a Smart Collection as their exact anchor and manual collections remain explicit ordered lists.

The catalogue/query path already exposes revision width and height, and the image decoder applies EXIF orientation during decode. However, maintained catalogue history includes revisions whose original `width`/`height` are null, while durable review proxies retain usable rendered geometry. The implementation must therefore not assume every existing revision can be classified from `asset_revisions.width > height` alone.

## Scope

- Add an optional Smart Collection orientation criterion with `Any`, `Landscape` and `Portrait` user choices.
- Persist the criterion in saved Smart Collection definitions and carry it through API/Web contracts, navigation state and query execution.
- Define orientation from visually correct rendered geometry, including EXIF-rotated source photos.
- Keep existing saved definitions backward compatible: absent orientation means `Any` and does not alter current membership.
- Handle revisions whose original dimensions are absent without hydrating originals solely for orientation classification. Reuse trustworthy durable derived geometry such as review-proxy dimensions when necessary.
- Define conservative semantics for square and unknown geometry. Initial product behavior should keep them in `Any` only unless implementation evidence supports a clearer separate category.
- Ensure Creative Collections automatically inherit the orientation constraint through their Smart Collection anchor; do not add a duplicate Creative recipe setting.
- Keep orientation filtering deterministic and suitable for normal archive-scale Smart Collection queries.

## Architecture constraints

Orientation is derived photo geometry, not editable archive truth. Do not modify source images or invent original dimensions from proxy dimensions. If proxy geometry is used as an aspect-ratio surrogate, keep that distinction explicit in code and tests.

A persisted/versioned derived orientation value may be introduced if query-time fallback proves unnecessarily expensive, but avoid a schema/backfill merely for convenience without measuring the query path first.

## Out of scope

- Slideshow screen-orientation preferences; those remain global playback settings.
- Cropping or rotating originals.
- Arbitrary aspect-ratio ranges.
- Treating square photos as landscape or portrait without an explicit product decision.

## Acceptance criteria

- [x] A user can create, preview, save and reopen a Smart Collection filtered to Landscape or Portrait photos.
- [x] Existing Smart Collections without the new field retain identical membership and display as `Any`.
- [x] Orientation filtering affects normal Smart Collection results and subsequent slideshow/Creative anchor membership consistently.
- [x] EXIF-rotated images are classified by visual orientation rather than uncorrected storage orientation.
- [x] Existing revisions with missing original dimensions are handled deterministically without original hydration solely for classification.
- [x] Square/unknown photos have documented conservative behavior and do not silently drift between Portrait and Landscape.
- [x] Persistence, PostgreSQL query, API contract/navigation and Web behavior have focused automated coverage.
- [x] Maintainer verifies representative portrait, landscape, EXIF-rotated and existing-catalogue examples on the real archive.

## Implementation notes

- Core now normalizes the criterion to `any`, `landscape` or `portrait`; absent values remain `any` so legacy definitions retain their previous membership.
- The existing filter schema version remains v3 because orientation is an additive optional JSON property. This avoids a catalogue migration/backfill while preserving compatibility with existing v1-v3 rows.
- PostgreSQL derives effective visual geometry set-wise. When a durable review proxy exists, its rendered width/height is authoritative for aspect-ratio classification because proxy pixels are produced after decoder EXIF auto-orientation. Catalogue `asset_revisions.width/height` is the fallback when no proxy is available.
- Proxy geometry is used only as an aspect-ratio surrogate. It is not written back as original image geometry.
- Landscape requires `width > height`; Portrait requires `width < height`. Square and unknown geometry therefore match only `Any`.
- API definition updates preserve a saved orientation when an older client omits the new optional field. New definitions and transient queries default an omitted field to `Any`.
- Web editor state, saved-definition summaries and transient browser-tab navigation all carry the orientation value.
- Creative Collection direct anchors inherit the criterion through the existing saved Smart Collection slideshow snapshot. No duplicate Creative recipe option was added.

## Automated verification

- Core tests cover defaulting, normalization and invalid orientation rejection.
- Live PostgreSQL coverage exercises catalogue-only Landscape/Portrait geometry, square and unknown exclusions, missing-original proxy fallback, proxy override of uncorrected source geometry, persisted/reopened definitions, legacy JSON without the field, and saved slideshow snapshot membership.
- API integration coverage verifies query request/response propagation, invalid-value rejection, and preservation of orientation when an older update request omits the field.
- Navigation/Web coverage verifies transient-state round trips, request construction, editor options/summaries, absence of a duplicate Creative control, and the EXIF `AutoOrient` decoder contract.

## Maintainer acceptance — 2026-10-04 (Europe/Stockholm)

The maintainer verified the implemented Smart Collection orientation workflow and reports WI-0179 works as expected. The representative archive checks, save/reopen behavior, Smart slideshow/Creative anchor behavior and desktop/phone presentation are accepted. WI-0179 is complete.
