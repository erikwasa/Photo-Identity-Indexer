---
id: WI-0072
title: Integrate archive photo metadata and enrich Photo Details
milestone: M19
status_source: ../status/work-items.yaml
depends_on: [WI-0050, WI-0064, WI-0065]
related_adrs: [ADR-0007]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Source.Local, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0072: Integrate archive photo metadata and enrich Photo Details

## Objective

Make capture-metadata inspection a normal safe archive-lifecycle step so newly processed photos persist photographic date/GPS before Photo Identity releases managed local content, and expose useful structured plus diagnostic metadata in Photo Details.

This closes the ingestion gap discovered during the 2026-08-19 M19 maintainer pass and unblocks end-to-end verification of automatic GPS-to-GeoNames enrichment.

## Contract

- Metadata inspection remains revision-bound and never modifies originals.
- A revision is inspected only while its exact original is already local and hash-verified. Metadata extraction must not independently hydrate an online-only original.
- Archive advancement should inspect metadata before releasing Photo Identity-managed hydration when possible.
- The existing explicit metadata-backfill endpoint remains available for historical catalogue rows and repair/retry.
- Persist an inspection record even when no supported metadata values are found, so `not inspected` is distinct from `inspected, empty`.
- Persist current query-critical fields: photographic capture wall-clock time, original UTC offset and atomic GPS latitude/longitude.
- Add useful structured fields where available: camera make/model, lens model, orientation, exposure time, aperture, ISO, focal length, 35 mm equivalent, flash description and GPS altitude.
- Persist a bounded sanitized raw metadata snapshot for inspection/debugging. Binary payloads, embedded previews/thumbnails and unbounded values must not be stored.
- Photo Details shows key metadata including capture date/time, camera/lens, exposure fields and exact GPS coordinates without exposing private source paths.
- Photo Details provides a collapsible all-metadata list sourced from the sanitized persisted snapshot; it never opens the original merely to render the page.
- GeoNames remains asynchronous and independent: once GPS is persisted, the existing worker can enrich Places without archive processing waiting for provider/network pacing.

## Acceptance criteria

- [ ] A newly archived local/hash-verified revision is metadata-inspected automatically before managed release when archive advancement encounters it.
- [ ] Automatic inspection does not hydrate online-only originals outside the existing archive hydration policy.
- [ ] Existing explicit backfill uses the same metadata inspection/persistence path rather than duplicating extraction logic.
- [ ] `DateTimeOriginal`, source offset and GPS continue to preserve WI-0050 semantics.
- [ ] JPEG and HEIC/HEIF metadata readers are covered with representative fixtures or deterministic metadata-reader tests.
- [ ] Camera make/model, lens model and common exposure metadata are persisted when present.
- [ ] GPS altitude is persisted when present and latitude/longitude remain atomic.
- [ ] A bounded sanitized raw tag snapshot is persisted without binary payloads or private paths.
- [ ] Photo Details shows inspection state, key capture/camera/GPS metadata and an all-metadata section from SQLite only.
- [ ] A newly persisted GPS row becomes eligible for existing automatic GeoNames enrichment without blocking archive advancement.
- [ ] Relevant unit/integration tests pass and living/generated documentation validates.

## Implementation slices

1. Extend the metadata model/reader and persistence schema, and factor one reusable verified-local metadata inspection service used by explicit backfill.
2. Integrate inspection into archive advancement before managed release and cover archive lifecycle behavior.
3. Extend Photo Details API/UI with structured metadata, exact GPS and sanitized all-tags display.
4. Run CI plus focused maintainer verification on a new archive folder containing date/GPS-bearing JPEG and HEIC photos; then complete WI-0064/WI-0065 if automatic GeoNames pickup/restart behavior passes.
