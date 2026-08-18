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

The implementation uses `findNearbyPlaceNameJSON` through the secure GeoNames host. The provider-client request interval is configurable so the service contract can be tuned without changing catalogue semantics. WI-0065 adds a separate conservative floor for unattended automatic processing.

## Place normalization

Provider output must be normalized into Photo Identity's canonical location hierarchy rather than stored as an opaque provider response.

A representative result is:

```text
Places/Sweden/Stockholm region/Norrtälje
```

The hierarchy is conceptually country -> available administrative subdivision(s) -> populated locality. The exact number of administrative levels may vary by country, so the implementation must omit unavailable/duplicate segments rather than force every country into an identical `Country/State/City` shape.

Store provider identifiers and provenance needed to explain or safely refresh an automatically derived place, but use Photo Identity's canonical place path as the Smart Collection query value.

Provider-derived administrative hierarchies can legitimately exceed the ordinary 80-character manual-tag path limit. First-class `Places/...` values therefore use a separate 500-character canonical hierarchy capacity while ordinary non-Places tag input remains capped at 80 characters.

## Implementation slices

### Slice 1 — provider, persistence and bounded enrichment foundation

Merged PR #160 established the non-UI enrichment path:

- a provider-neutral `IReverseGeocoder` boundary in Core isolates catalogue/location semantics from GeoNames HTTP handling;
- `GeoNamesReverseGeocoder` uses the secure JSON service, rejects the public `demo` account and non-HTTPS base URLs, sends only coordinates plus documented provider parameters and the configured username, normalizes country/admin/locality values into `PhotoPlacePath`, and maps quota/transient provider states to clean deferral;
- private startup configuration uses `PhotoIdentity:GeoNames:Username`, optional `BaseUrl`, optional `Language`, and configurable `MinimumRequestIntervalMilliseconds`;
- catalogue schema v15 persists coordinate/provider-contract cache rows and per-revision `succeeded`/`skipped`/`deferred`/`failed` enrichment attempts;
- candidate selection reads only persisted `photo_capture_metadata` GPS and records successful or terminal-skip state so bounded runs make forward progress; deferred/failed attempts remain retryable;
- identical coordinates under the same provider contract reuse a cached normalized result unless an explicit refresh is requested;
- automatic place assignment has a dedicated write boundary: any latest manual set **or manual clear** blocks automatic enrichment, as does an unresolved WI-0063 migration conflict; a later automatic refresh may replace an earlier automatic place through append-only place history;
- `/api/place-enrichment/status` exposes non-secret provider configuration state and `/api/place-enrichment/geonames` executes an explicit bounded batch with operator-useful result counts;
- automated coverage uses fake HTTP/provider implementations only and verifies normalization/privacy request shape, quota deferral, cache reuse, manual-clear precedence, retry, automatic refresh and no source-file access.

### Slice 2 — operator workflow, attribution and final M19 verification handoff

Merged PR #161 added the operator-facing workflow:

- Settings reports configured/disabled provider state, service host, language and request pacing without returning the configured GeoNames username;
- the maintainer can choose a bounded candidate batch and explicitly run normal enrichment or force-refresh automatic places;
- normal execution explains cache reuse/resumability, while force refresh states that it bypasses cached reverse-geocode results and can spend additional provider credits;
- the operator sees the latest in-session candidates, provider requests, cache reuse, assignment, protected manual/conflict skip, deferred/failure and early-stop counts;
- the Settings surface states that persisted latitude/longitude, the configured GeoNames username and documented service parameters leave the machine when enrichment is invoked, while photo bytes, filenames, source paths, people, tags and other catalogue metadata do not;
- GeoNames attribution is presented directly with the provider-derived place workflow;
- endpoint coverage verifies username redaction, disabled-provider refusal and the server-side batch bound without live provider calls.

### Live-provider corrective slice

Maintainer verification on 2026-08-17 exposed three operator/runtime issues that were corrected in PR #165:

- the first live request failed because the GeoNames account had not enabled **Free Web Services**; the provider error was persisted but the Settings report discarded its reason and incorrectly presented the failed run as successful completion;
- after web-service access was enabled, GeoNames enrichment successfully assigned correct automatic Places and Smart Collection location filtering found the assigned photo;
- three live revisions (`IMG_0181.HEIC`, `IMG_0185.HEIC`, `IMG_0184.HEIC`) failed with `invalid-place-path` because their valid provider-derived `Places/...` hierarchies exceeded the generic 80-character photo-tag path limit;
- the first-class Places parser and persisted Places/reverse-geocode storage now allow canonical hierarchies up to 500 characters, while ordinary non-Places tag input remains limited to 80 characters;
- the existing SQLite catalogue is widened idempotently at startup, preserving existing tag IDs, place actions, enrichment attempts and cache rows; failed attempts remain retryable and require no manual catalogue cleanup;
- operator reporting now preserves sanitized per-photo provider outcomes with an **Open photo** link, and genuine provider `no result` outcomes are distinguished from failures and completed terminally so normal runs do not spend credits retrying the same coordinates.

### Post-merge live verification and orchestration follow-up

On 2026-08-18 the maintainer reran the previously failed long-hierarchy revisions against the live GeoNames service. All three succeeded after the compatibility fix, confirming that the persisted `failed` rows were selected again and that valid provider hierarchies beyond the old 80-character limit can now be cached, persisted and assigned.

The same session exposed an orchestration issue rather than a provider/catalogue failure: a large 200-candidate manual browser request with a one-second configured provider delay exceeded the browser HTTP timeout after roughly 100 seconds. Rows completed before request cancellation remained durable; later rows simply remained eligible and subsequent smaller runs continued successfully. A query for persisted `failed`/`deferred` attempts returned no rows after the successful retries.

That browser-lifetime mismatch is moved to [WI-0065](WI-0065-automatic-place-enrichment.md). WI-0065 makes the normal workflow a server-side automatic worker that drains the existing durable queue independently of browser requests and archive analysis.

## In scope

- Add a reverse-geocoding abstraction with a GeoNames implementation so provider-specific HTTP/parsing logic is isolated from catalogue/location semantics.
- Configure the GeoNames username and service settings through private local Photo Identity configuration; do not commit real credentials/account data.
- Use the secure GeoNames endpoint and documented reverse-geocoding services appropriate for obtaining country, administrative region(s) and nearest populated locality.
- Read latitude/longitude only from persisted `photo_capture_metadata`; reverse geocoding must not open source photos.
- Add a bounded/resumable enrichment operation for revisions with GPS but no completed GeoNames place attempt for the current provider/configuration contract.
- Persist successful normalized results plus enough provenance to distinguish `manual` from `geonames` location assignment and to support retries/audit.
- Cache/reuse completed reverse-geocoding results for identical coordinates/provider inputs where safe, so repeated runs do not spend unnecessary service credits.
- Rate-limit outbound requests and stop/defer cleanly when provider limits, transient errors or network failures occur.
- Leave failed/deferred revisions eligible for retry rather than inventing a place.
- Never replace a current manual place automatically. Manual corrections are authoritative until a maintainer explicitly chooses otherwise.
- If an automatically derived result becomes more specific on a later explicit refresh, replace the previous automatic place using WI-0063's single-place semantics.
- Add operator-visible reporting for candidates, successful assignments, cached/reused results, skipped manual places, no-result outcomes, deferred requests and failures.
- Add GeoNames attribution in the application/operator documentation where provider-derived place data is presented or described.
- Provide a controlled way to refresh/re-run automatic place enrichment after GPS metadata or provider interpretation changes.

## Privacy and safety boundary

Reverse geocoding sends latitude/longitude to GeoNames. WI-0064 introduced it as an explicitly configured and manually invoked operation. WI-0065 keeps private username configuration as the explicit opt-in but changes normal execution to automatic background processing once configured; Settings must make that continuing external-GPS behavior clear.

The operation does not send photo bytes, filenames, people, tags, source paths or other catalogue information to GeoNames. The configured GeoNames username is provider authentication/configuration and is not returned by the Photo Identity status API or displayed by the browser.

## Out of scope

- Downloading or maintaining GeoNames database extracts locally.
- OpenStreetMap/Nominatim as the production provider for this work item.
- Forward geocoding arbitrary typed addresses.
- Reverse geocoding photos without GPS metadata.
- Replacing a maintainer-entered manual place without explicit maintainer action.
- Map tiles, maps, route planning or administrative polygon storage.

## Acceptance criteria

- [x] GeoNames web-service API is the implemented reverse-geocoding provider and no GeoNames database extract is required.
- [x] GeoNames configuration is private/local, uses the secure service endpoint and does not rely on the public demo account.
- [x] Reverse geocoding operates from persisted GPS coordinates and never opens or hydrates the original photo.
- [x] A successful response is normalized into the WI-0063 canonical place hierarchy with country, available administrative levels and populated locality as available.
- [x] Provider-derived first-class Places are not constrained by the ordinary 80-character manual-tag path limit; existing catalogues are widened without recreation.
- [x] The operation is bounded and resumable, with rate limiting and retry-safe handling of provider/network failures.
- [x] Completed results can be cached/reused without unnecessary repeated provider requests.
- [x] Automatic enrichment never silently overwrites a manual place, including an explicit manual clear.
- [x] A later more-specific automatic result can replace an earlier automatic place while retaining provenance/audit history.
- [x] Photos without GPS remain unassigned rather than receiving inferred or fabricated locations.
- [x] Outbound requests contain coordinates/provider parameters only and do not disclose photo bytes, filenames, people, tags or private source paths.
- [x] GeoNames attribution and external-GPS privacy behavior are documented and presented for the operator.
- [x] Automated tests use a fake/stub GeoNames HTTP boundary and cover normalization, caching, retries, rate-limit/error handling, manual precedence, long provider hierarchies and no-hydration behavior.
- [x] The three live `invalid-place-path` revisions were retried successfully after PR #165, confirming the long Places path fix against the configured maintainer GeoNames account.
- [ ] The consolidated M19 browser/operator pass is recorded as final verification evidence.

## Verification requirements

Automated provider-contract and catalogue tests must not depend on the live GeoNames service. The live maintainer sample has now established provider access, geographically correct automatic assignment, Smart Collection location filtering and successful retry of the three former long-path failures. Remaining M19 verification can concentrate on the consolidated browser workflow plus WI-0065 automatic orchestration rather than repeating the same manual large-batch GeoNames exercise.
