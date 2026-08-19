---
id: WI-0072
title: Integrate archive photo metadata and enrich Photo Details
milestone: M19
status_source: ../status/work-items.yaml
depends_on: [WI-0050]
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
- Bounded archive advancement ensures metadata has been inspected before an exact revision proceeds into analysis, review-proxy generation or Photo Identity-managed release.
- The existing explicit metadata-backfill endpoint remains available for historical catalogue rows and repair/retry.
- Persist an inspection record even when no supported metadata values are found, so `not inspected` is distinct from `inspected, empty`.
- Persist current query-critical fields: photographic capture wall-clock time, original UTC offset and atomic GPS latitude/longitude.
- Add useful structured fields where available: camera make/model, lens model, orientation, exposure time, aperture, ISO, focal length, 35 mm equivalent, flash description and GPS altitude.
- Persist a bounded sanitized raw metadata snapshot for inspection/debugging. Binary payloads, embedded previews/thumbnails and unbounded values must not be stored.
- Photo Details shows key metadata including capture date/time, camera/lens, exposure fields and exact GPS coordinates without exposing private source paths.
- Photo Details provides a collapsible all-metadata list sourced from the sanitized persisted snapshot; it never opens the original merely to render the page.
- GeoNames remains asynchronous and independent: once GPS is persisted, the existing worker can enrich Places without archive processing waiting for provider/network pacing.

## Acceptance criteria

- [ ] A newly archived local/hash-verified revision is metadata-inspected automatically before managed release when archive advancement encounters it. The bounded advancement guard is implemented; final proof uses the maintainer's real archive-media pass.
- [x] Automatic inspection does not hydrate online-only originals outside the existing archive hydration policy.
- [x] Existing explicit backfill uses the same metadata inspection/persistence path rather than duplicating extraction logic.
- [x] `DateTimeOriginal`, source offset and GPS continue to preserve WI-0050 semantics.
- [ ] JPEG and HEIC/HEIF metadata readers are covered with representative fixtures or deterministic metadata-reader tests. Deterministic JPEG/XMP fallback coverage is implemented; a representative real iPhone HEIC remains a maintainer verification input.
- [x] Camera make/model, lens model and common exposure metadata are persisted when present.
- [x] GPS altitude is persisted when present and latitude/longitude remain atomic.
- [x] A bounded sanitized raw tag snapshot is persisted without binary preview/thumbnail payloads or private source paths.
- [x] Photo Details shows inspection state, key capture/camera/GPS metadata and an all-metadata section from SQLite only.
- [ ] A newly persisted GPS row becomes eligible for existing automatic GeoNames enrichment without blocking archive advancement. The persistence/worker boundary is unchanged; final proof belongs to the live WI-0064/WI-0065 maintainer pass.
- [ ] Relevant unit/integration tests pass and living/generated documentation validates on the final exact PR head.

## Implementation

### Shared inspection and persistence

`PhotoMetadataInspectionService` now owns the durable inspection write boundary. It receives an already-local verified stream, extracts metadata once, stores richer presentation metadata, then writes the existing WI-0050 `photo_capture_metadata` row last. That capture row remains the durable inspection-complete marker, including the legitimate `inspected, empty` case.

The WI-0050 query-critical capture-time/GPS table is intentionally unchanged. Richer camera/exposure/raw metadata is stored in `photo_extended_metadata`, initialized explicitly at application startup through `SqliteExtendedPhotoMetadataSchema`. This avoids changing Smart Collection and GeoNames query contracts merely to add presentation fields.

The explicit `/api/photo-metadata/backfill` operation reuses the same inspection service after its existing local-state, size and SHA-256 checks. It still never requests hydration.

### Archive lifecycle

The metadata invariant is enforced in `ArchiveBoundedAnalysisService`, not only in source verification. This is important because already-local archive files can establish an immutable revision directly during synchronization and therefore do not necessarily pass through source-verification advancement.

Before queued/resumed analysis, new analysis, post-analysis proxy generation or managed release proceeds for an exact revision, bounded advancement calls the shared inspection guard. The guard opens the original through the existing exact-revision verification boundary; if the original no longer matches, the revision returns to source verification rather than receiving stale metadata.

This creates no new hydration policy. Online-only originals are hydrated only by the existing bounded archive workflow when analysis/proxy work already requires them; metadata is consumed while that exact content is local.

### Reader and metadata shape

The MetadataExtractor adapter now captures:

- EXIF `DateTimeOriginal` and original timezone offset;
- GPS latitude/longitude and altitude;
- camera make/model;
- lens model;
- orientation;
- exposure time;
- aperture;
- ISO;
- focal length and 35 mm equivalent;
- flash description;
- a maximum of 300 sanitized directory/tag/value rows with bounded text lengths.

Embedded thumbnail/preview/image payloads, maker-note payloads and ICC-profile payloads are excluded from the raw snapshot. MetadataExtractor's HEIF reader already exposes embedded HEIC EXIF through the normal EXIF directories; the Photo Identity adapter also records real XMP properties and uses XMP capture-date/camera fields as fallbacks when EXIF is absent. XMP timestamps with an explicit offset retain the wall-clock value and persist the offset separately; timezone-less timestamps remain timezone-less.

### Photo Details

The Photo Details repository/API now return persisted metadata from SQLite only. A missing capture row is `Not inspected`; an empty persisted capture row is `Inspected` with no supported key fields. The viewer shows capture time, camera/lens, exposure fields, exact GPS coordinates/altitude and a collapsible bounded `All metadata` table without opening the original during page rendering.

## Automated coverage

- `PhotoMetadataBackfillServiceTests` verifies local-only/hash-verified shared inspection, richer persistence and the no-hydration behavior for online-only candidates.
- `PhotoDetailsMetadataApplicationTests` reuses the shared API host and verifies the structured/raw Photo Details contract plus the distinction between not-inspected and inspected-empty.
- `MetadataExtractorPhotoMetadataReaderTests` uses a deterministic synthetic JPEG/XMP packet to verify capture wall-clock/offset semantics, camera fallback and raw XMP capture without adding heavyweight media-generation dependencies.
- Existing archive, published-review, mixed-media, launcher/package and required integration suites remain the surrounding regression gate.

## Remaining verification

1. Obtain a green exact-head CI run for the final branch state.
2. Run a focused maintainer pass on a newly included archive folder containing at least one known date/GPS-bearing JPEG and one representative iPhone HEIC/HEIF file.
3. Confirm Photo Details shows the expected date, camera metadata and exact GPS for those files.
4. Without manually invoking metadata backfill or place enrichment, confirm the persisted GPS reaches the automatic GeoNames worker and receives the expected Place.
5. Restart with a retryable/outstanding GeoNames revision and confirm WI-0065 durable resume behavior.

Only after those real-media/provider checks should WI-0072, WI-0064 and WI-0065 be completed and M19 closed.
