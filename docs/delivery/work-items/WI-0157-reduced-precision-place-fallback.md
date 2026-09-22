---
id: WI-0157
title: Add reduced-precision place fallback for GPS photos without populated-place matches
milestone: M28
status_source: ../status/work-items.yaml
depends_on: [WI-0064, WI-0065]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Persistence.Tests, PhotoIdentity.Integration.Tests, docs]
---

# WI-0157: Add reduced-precision place fallback for GPS photos without populated-place matches

## Objective

Recover useful automatic Place assignments for photos that have persisted GPS coordinates but for which the current GeoNames populated-place lookup returns no result, by falling back to less precise administrative geography without inventing a locality.

## Why

The representative catalogue contains 17,863 current images. Initial diagnostics reported 1,337 images with persisted coordinates but no effective named Place, and those rows motivated the reduced-precision fallback. Maintainer verification after the fallback shipped established that all 1,337 coordinate pairs were exactly `(0,0)`, so the original backlog was placeholder/invalid GPS rather than genuine unresolved geographic positions.

A country, region, county or municipality-level assignment remains useful for genuine GPS positions where populated-place lookup fails. Exact `(0,0)` must instead be treated as missing GPS so it never spends provider requests or appears as a real location candidate.

## In scope

- Keep the current populated-place lookup as the preferred, more precise path.
- When GeoNames returns a genuine populated-place `no-result`, attempt a provider-supported administrative fallback such as country/subdivision hierarchy.
- Build the canonical `Places/...` path only from administrative segments actually returned by the provider.
- Accept reduced precision, including country-only or intermediate administrative hierarchy, when that is all the provider can establish.
- Preserve automatic provenance and make the fallback contract/version distinguishable in enrichment state.
- Requeue prior genuine terminal `no-result` attempts in a controlled bounded way when provider behavior changes.
- Normalize exact `(0,0)` capture coordinates to missing GPS at the domain boundary.
- Migrate existing `(0,0)` capture metadata to `NULL,NULL` and prevent PostgreSQL from accepting the pair again.
- Exclude `(0,0)` defensively from enrichment candidate queries in both supported persistence adapters.
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
- [x] Real-catalogue verification after zero-zero normalization shows the current 1,337 placeholder coordinate rows no longer count as GPS, no non-zero GPS-without-Place candidates remain in this catalogue, and manually assigned Places remain unchanged.

## Verification requirements

Run normal CI plus the live PostgreSQL acceptance suite. On the representative catalogue, restart against the upgraded schema and confirm the 1,337 exact `(0,0)` capture rows are normalized to `NULL,NULL`, the GPS-without-Place count falls to zero, and the three existing manual Places remain unchanged. For any future genuine non-zero GPS/no-Place candidates, verify fallback assignments in Photo Details/Smart Collections and confirm that displayed hierarchy is useful without claiming a more precise locality than the provider returned. Do not record private coordinates or source paths in repository evidence.

## Completion notes

- Files changed: GeoNames reverse geocoder, PostgreSQL/SQLite enrichment candidate selection, integration tests, PostgreSQL persistence acceptance coverage and delivery status.
- Trade-offs: the fallback uses GeoNames `countrySubdivisionJSON` only after `findNearbyPlaceNameJSON` returns a genuine no-result, then `countryCodeJSON` only when no subdivision is available. This preserves precise locality when available and deliberately accepts country/admin-only hierarchy rather than expanding the nearby-town radius.
- Contract transition: `geonames-place-v3` makes the fallback behavior explicit. A new contract retries prior non-success outcomes, including the recorded legacy `no-result` rows, while unchanged coordinates with any prior successful contract are suppressed unless an operator explicitly requests refresh.
- Follow-up correction: maintainer verification proved all 1,337 originally reported GPS/no-Place rows were exactly `(0,0)`. The follow-up normalizes that pair to missing GPS in Core, cleans existing PostgreSQL/SQLite catalogue rows, adds a PostgreSQL guard constraint and excludes the pair from enrichment candidates defensively.
- Deferred work: photos without genuine GPS remain outside automatic reverse-geocoding scope. The current catalogue contains no non-zero GPS/no-Place candidates with which to demonstrate a real fallback assignment.
- Verification: PR #403 passed repository workflow run #2080. Maintainer verification on 2026-09-22 confirmed PostgreSQL schema v30 applied, exact `(0,0)` capture rows were removed from the GPS population, no non-zero GPS-without-Place candidates remained, the PostgreSQL guard constraint was present, existing Place assignments were preserved, and automatic enrichment settled without the placeholder backlog.
- Status: completed on 2026-09-22 after maintainer acceptance.
