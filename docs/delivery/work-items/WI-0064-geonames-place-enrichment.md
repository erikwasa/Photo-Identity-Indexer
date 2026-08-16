---
id: WI-0064
title: Add GeoNames reverse-geocoded Places enrichment
milestone: M19
status_source: ../status/work-items.yaml
depends_on: [WI-0050, WI-0063]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0064: Add GeoNames reverse-geocoded Places enrichment

## Objective

Use the GeoNames web-service API to derive a canonical `Places/...` hierarchy from already-persisted GPS coordinates and assign that place through the first-class place model from WI-0063, without opening or hydrating the original photo.

## Provider decision

GeoNames is the selected first reverse-geocoding provider for M19 follow-up work.

- Use the GeoNames **web-service API**, not downloaded GeoNames database extracts.
- Use GeoNames REST/JSON reverse-geocoding services over HTTPS.
- Supply a configured GeoNames username with requests as required by the service.
- Do not use the public `demo` account for application operation or automated tests.
- Respect the provider's current credit/rate limits and attribution requirements; implementation must not hard-code assumptions that prevent using different limits or a paid GeoNames service later.

## Place normalization

Provider output must be normalized into Photo Identity's canonical location hierarchy rather than stored as an opaque provider response.

A representative result is:

```text
Places/Sweden/Stockholm region/Norrtälje
```

The hierarchy is conceptually country -> available administrative subdivision(s) -> populated locality. The exact number of administrative levels may vary by country, so the implementation must omit unavailable/duplicate segments rather than force every country into an identical `Country/State/City` shape.

Store provider identifiers and provenance needed to explain or safely refresh an automatically derived place, but use Photo Identity's canonical place path as the Smart Collection query value.

## In scope

- Add a reverse-geocoding abstraction with a GeoNames implementation so provider-specific HTTP/parsing logic is isolated from catalogue/location semantics.
- Configure the GeoNames username and service settings through private local Photo Identity configuration; do not commit real credentials/account data.
- Use the secure GeoNames endpoint and documented reverse-geocoding services appropriate for obtaining country, administrative region(s) and nearest populated locality.
- Read latitude/longitude only from persisted `photo_capture_metadata`; reverse geocoding must not open source photos.
- Add an explicit bounded/resumable enrichment operation for revisions with GPS but no completed GeoNames place attempt for the current provider/configuration contract.
- Persist successful normalized results plus enough provenance to distinguish `manual` from `geonames` location assignment and to support retries/audit.
- Cache/reuse completed reverse-geocoding results for identical coordinates/provider inputs where safe, so repeated runs do not spend unnecessary service credits.
- Rate-limit outbound requests and stop/defer cleanly when provider limits, transient errors or network failures occur.
- Leave failed/deferred revisions eligible for retry rather than inventing a place.
- Never replace a current manual place automatically. Manual corrections are authoritative until a maintainer explicitly chooses otherwise.
- If an automatically derived result becomes more specific on a later explicit refresh, replace the previous automatic place using WI-0063's single-place semantics.
- Add operator-visible reporting for candidates, successful assignments, cached/reused results, skipped manual places, deferred requests and failures.
- Add GeoNames attribution in the application/operator documentation where provider-derived place data is presented or described.
- Provide a controlled way to refresh/re-run automatic place enrichment after GPS metadata or provider interpretation changes.

## Privacy and safety boundary

Reverse geocoding sends latitude/longitude to GeoNames. It must therefore be disabled until explicitly configured and invoked, and operator documentation must state that GPS coordinates are sent to the external GeoNames service during enrichment.

The operation must not send photo bytes, filenames, people, tags, source paths or other catalogue information to GeoNames.

## Out of scope

- Downloading or maintaining GeoNames database extracts locally.
- OpenStreetMap/Nominatim as the production provider for this work item.
- Forward geocoding arbitrary typed addresses.
- Automatically reverse geocoding photos without GPS metadata.
- Replacing a maintainer-entered manual place without explicit maintainer action.
- Map tiles, maps, route planning or administrative polygon storage.

## Acceptance criteria

- [ ] GeoNames web-service API is the implemented reverse-geocoding provider and no GeoNames database extract is required.
- [ ] GeoNames configuration is private/local, uses the secure service endpoint and does not rely on the public demo account.
- [ ] Reverse geocoding operates from persisted GPS coordinates and never opens or hydrates the original photo.
- [ ] A successful response is normalized into the WI-0063 canonical place hierarchy with country, available administrative levels and populated locality as available.
- [ ] The operation is explicit, bounded and resumable, with rate limiting and retry-safe handling of provider/network failures.
- [ ] Completed results can be cached/reused without unnecessary repeated provider requests.
- [ ] Automatic enrichment never silently overwrites a manual place.
- [ ] A later more-specific automatic result can replace an earlier automatic place while retaining provenance/audit history.
- [ ] Photos without GPS remain unassigned rather than receiving inferred or fabricated locations.
- [ ] Outbound requests contain coordinates/provider parameters only and do not disclose photo bytes, filenames, people, tags or private source paths.
- [ ] GeoNames attribution and external-GPS privacy behavior are documented for the operator.
- [ ] Automated tests use a fake/stub GeoNames HTTP boundary and cover normalization, caching, retries, rate-limit/error handling, manual precedence and no-hydration behavior.

## Verification requirements

Automated provider-contract and catalogue tests must not depend on the live GeoNames service. Final local verification should use a configured maintainer GeoNames account against a small bounded sample of GPS-tagged photos and compare several resulting place paths with expected real-world locations.
