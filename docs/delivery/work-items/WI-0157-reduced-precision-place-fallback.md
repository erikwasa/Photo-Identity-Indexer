---
id: WI-0157
title: Add reduced-precision place fallback for GPS photos without populated-place matches
milestone: M28
status_source: ../status/work-items.yaml
depends_on: [WI-0064, WI-0065]
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Persistence.Tests, PhotoIdentity.Integration.Tests, docs]
---

# WI-0157: Add reduced-precision place fallback for GPS photos without populated-place matches

## Objective

Recover useful automatic Place assignments for photos that have persisted GPS coordinates but for which the current GeoNames populated-place lookup returns no result, by falling back to less precise administrative geography without inventing a locality.

## Why

The accepted production catalogue currently contains 17,863 current images. Of these, 1,337 have persisted GPS coordinates but no effective named Place. All 1,337 were processed exactly once by automatic GeoNames enrichment and were terminally marked `skipped` with `no-result` because `findNearbyPlaceNameJSON` returned no populated place.

A country, region, county or municipality-level assignment is still useful for family-library browsing and Smart Collections. The current all-or-nothing populated-place lookup discards that useful lower-precision evidence.

## In scope

- Keep the current populated-place lookup as the preferred, more precise path.
- When GeoNames returns a genuine populated-place `no-result`, attempt a provider-supported administrative fallback such as country/subdivision hierarchy.
- Build the canonical `Places/...` path only from administrative segments actually returned by the provider.
- Accept reduced precision, including country-only or intermediate administrative hierarchy, when that is all the provider can establish.
- Preserve automatic provenance and make the fallback contract/version distinguishable in enrichment state.
- Requeue the existing terminal `no-result` population in a controlled bounded way so the current 1,337-photo backlog can benefit from the fallback.
- Avoid unnecessarily re-querying photos that already have successful effective automatic/manual Places solely because this fallback is introduced.
- Preserve manual set/clear precedence, migration-conflict blocking, provider pacing, cache semantics and retry/defer behavior.
- Report fallback outcomes separately enough for operator verification without exposing private coordinates or source paths.

## Out of scope

- Guessing a nearby town by dramatically increasing search radius.
- Inferring location for photos without GPS.
- Image-content, caption, folder-name, time-neighbor or person-based location inference.
- Writing EXIF/XMP or other metadata back to originals.
- Replacing GeoNames with a different provider as part of this item.
- Adding arbitrary location-confidence scoring.

## Acceptance criteria

- [x] A persisted-GPS photo that resolves through the existing populated-place lookup keeps the existing precise Place behavior.
- [x] A persisted-GPS photo with a populated-place `no-result` can receive an automatic country/admin hierarchy when GeoNames provides that hierarchy.
- [x] The stored Place never includes a locality/town segment that the fallback response did not actually establish.
- [x] Country-only and intermediate administrative results are valid automatic assignments rather than failures.
- [x] Manual set and manual clear remain authoritative and cannot be overwritten by fallback enrichment.
- [x] Unresolved migration conflicts continue to block automatic assignment.
- [x] Existing successful assignments are not broadly reprocessed just because the fallback contract is introduced.
- [x] Prior terminal `no-result` attempts can be re-evaluated once under the new fallback behavior without manual database edits.
- [x] If both populated-place and administrative fallback return no usable geography, the result remains terminally skipped with a reason distinguishable from the old populated-place-only outcome.
- [x] Provider/cache/attempt persistence remains restart-safe and bounded.
- [x] Automated tests cover precise success, administrative fallback at multiple hierarchy depths, fallback no-result, manual precedence, conflict blocking, cache reuse and legacy `no-result` requeue.
- [ ] Real-catalogue verification shows the GPS-without-Place population decreases from the recorded 1,337 baseline without changing manually assigned Places.

## Verification requirements

Run normal CI plus the live PostgreSQL acceptance suite. On the representative production catalogue, record aggregate counts before and after fallback enrichment: effective automatic/manual/migrated Places, GPS-without-Place, fallback assignments by hierarchy depth and remaining no-result rows. Inspect a small representative sample of fallback assignments in Photo Details/Smart Collections and confirm that displayed hierarchy is useful without claiming a more precise locality than the provider returned. Do not record private coordinates or source paths in repository evidence.

## Completion notes

- Files changed: GeoNames reverse geocoder, PostgreSQL/SQLite enrichment candidate selection, integration tests, PostgreSQL persistence acceptance coverage and delivery status.
- Trade-offs: the fallback uses GeoNames `countrySubdivisionJSON` only after `findNearbyPlaceNameJSON` returns a genuine no-result. This preserves precise locality when available and deliberately accepts country/admin-only hierarchy rather than expanding the nearby-town radius.
- Contract transition: `geonames-place-v3` makes the fallback behavior explicit. A new contract retries prior non-success outcomes, including the recorded legacy `no-result` rows, while unchanged coordinates with any prior successful contract are suppressed unless an operator explicitly requests refresh.
- Deferred work: photos without GPS remain outside automatic reverse-geocoding scope. Real-catalogue acceptance remains for maintainer verification against the recorded 1,337-photo baseline.
- Commands run: repository CI is the implementation validation surface; live production-catalogue verification remains maintainer-operated.
