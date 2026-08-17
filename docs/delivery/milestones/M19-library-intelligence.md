---
id: M19
title: Photo metadata and semantic collections
status_source: ../status/milestones.yaml
depends_on: [M12, M14]
---

# M19: Photo metadata and semantic collections

## Outcome

The catalogue can organize photos using photographic capture metadata, location, people and maintainer-owned hierarchical tags. Saved smart collections combine these dimensions and reevaluate against the current catalogue so newly ingested matching photos appear automatically.

The extended M19 scope also makes Photo Details a useful catalogue-inspection surface, supports manual photo-level people when no face was detected, treats `Places/` as a first-class single-valued location hierarchy, enriches GPS-bearing photos through the GeoNames web-service API, and keeps that enrichment running automatically in the server process rather than requiring repeated browser batches.

## Work items

- [WI-0056](../work-items/WI-0056-manual-photo-tags.md) — make manual photo tags hierarchical and compatible with Immich tag-path semantics while retaining SQLite as the source of truth
- [WI-0050](../work-items/WI-0050-exif-smart-collections.md) — ingest capture metadata and persist reusable smart collections over people, tags, location and taken-time filters
- [WI-0061](../work-items/WI-0061-photo-details-navigation-context.md) — show privacy-safe filename/people details and preserve the originating collection/navigation context
- [WI-0062](../work-items/WI-0062-manual-photo-people.md) — add revision-level manual person presence without fabricating face evidence
- [WI-0063](../work-items/WI-0063-first-class-places.md) — reserve `Places/`, enforce one effective place per revision and move named places into the Smart Collections Location dimension
- [WI-0064](../work-items/WI-0064-geonames-place-enrichment.md) — reverse geocode persisted GPS coordinates through the GeoNames API into canonical Places hierarchy
- [WI-0065](../work-items/WI-0065-automatic-place-enrichment.md) — continuously drain the persisted GeoNames queue in a provider-safe server-side worker so new/retryable GPS photos are enriched without a manual browser batch

Automatic visible-content tagging is intentionally deferred. [WI-0049](../work-items/WI-0049-visible-content-tagging-experiment.md) remains as a design/experiment note that can be revived later, but it is not part of the active M19 completion path.

## Verified baseline

WI-0056 and WI-0050 established the original M19 baseline: hierarchical manual tags, capture-time/GPS persistence, safe metadata backfill and persistent smart collections over people/tags/GPS/taken time. The maintainer completed the integrated baseline verification on 2026-08-16 and reported that M19 and its work-item functions behaved as expected.

The baseline work items remain complete. WI-0061 through WI-0065 extend M19 rather than reopening the already-verified WI-0050 contract.

## Tag and Places architecture

General manual tags follow Immich-style hierarchy semantics: a tag has a local `name`, a full-path `value`, an optional parent and an optional color. `/` is the hierarchy separator.

The extended scope reserves `Places` as a special root for location data. `Places/Sweden/Stockholm region/Norrtälje` is a canonical location hierarchy, but a photo has only one effective place assignment. Parent nodes remain reusable vocabulary rather than separate active assignments.

Places are not ordinary Smart Collection tags. They belong to the Location dimension, where selecting an ancestor such as Sweden matches descendant assignments. Normal UI does not need to show the literal `Places/` prefix.

Photo Identity continues to persist metadata and revision-bound actions in SQLite. It does not adopt XMP sidecar write-back. Originals remain read-only and metadata-only edits must not hydrate online-only files.

## People architecture

Confirmed face assignments remain face evidence. M19 follow-up work may also record that a canonical person is present in a photo even when no usable face was detected.

Photo-level manual person presence is revision-bound catalogue metadata and must remain separate from face occurrences, crops, embeddings, review assignments and identity suggestions. Smart Collections may use the union of confirmed face people and active manual photo-level people, while face-review/evidence workflows remain face-based.

## Smart-collection semantics

A smart collection is a persisted query definition, not a copied list of asset IDs. Results are evaluated from the current catalogue each time the collection is opened or queried, so new matching photos join automatically.

The definition can combine:

- people — one or more canonical people, with `all` or `any` matching inside the people dimension;
- tags — one or more non-Places canonical full tag values, with `all` or `any` matching inside the tag dimension;
- location — an optional canonical named place and/or optional GPS coordinate bounds;
- taken time — inclusive photographic date ranges derived from capture metadata.

Different dimensions combine with AND semantics. Named-place matching is hierarchical: selecting `Places/Sweden` matches that node and descendant places such as `Places/Sweden/Stockholm region/Norrtälje`.

## GeoNames enrichment

GeoNames is the selected M19 reverse-geocoding provider. Photo Identity uses the GeoNames web-service API rather than downloaded GeoNames database extracts.

Reverse geocoding operates only from GPS coordinates already persisted in SQLite and never opens or hydrates originals. Configuring a private GeoNames username is the explicit opt-in to external reverse geocoding. Once configured, the normal operating model is automatic: a server-side hosted service notices eligible persisted-GPS revisions, reuses the existing WI-0064 cache/attempt state and continues until no eligible work remains.

The GeoNames worker is independent of archive analysis. Archive/local processing persists capture metadata as quickly as possible; if GPS is present, that database row becomes eligible for the background reverse-geocoding queue. Archive processing does not wait on external GeoNames response times or provider pacing.

Automatic provider pacing must be conservative enough that the maintainer does not need to calculate free-service limits. As of 2026-08-18 GeoNames documents 10,000 credits/day and 1,000 credits/hour per username/application, and `findNearbyPlaceName` costs 3 credits per request. WI-0065 therefore applies a 30-second automatic request floor, while provider quota/overload/transport responses pause and retry automatically. Small manual maintenance/force-refresh actions remain available for diagnostics only.

Manual place corrections take precedence over automatic GeoNames results. Operator documentation must make clear that latitude/longitude are sent to the external GeoNames service during automatic enrichment and must include required provider attribution.

## Navigation semantics

Photo Details should preserve the context from which it was opened. In particular, opening a result from Smart Collections and returning through browser/mouse Back must restore the selected collection or transient preview, filters and result page rather than resetting the workspace. Photo Details should expose a context-aware Back action instead of always returning to `/collections`.

## Verification strategy

The original M19 baseline was verified on 2026-08-16. Each extension work item should have automated evidence plus its focused local verification. WI-0064 live-provider verification established real GeoNames normalization, Smart Collection integration and long hierarchy storage; WI-0065 verification focuses on orchestration: automatic pickup, safe pacing, restart/resume behavior and browser-independence.

After WI-0061 through WI-0065 are complete, perform one final integrated M19 extension pass covering navigation restoration, Photo Details metadata, manual photo-level people, first-class Places and unattended live GeoNames enrichment.

## Exit criteria

- Manual non-Places tags support hierarchical Immich-compatible values while remaining SQLite-backed and revision-audited.
- Capture time is stored with photographic local-time semantics instead of inventing UTC when the source metadata has no offset.
- GPS metadata is retained when available without making location mandatory.
- Smart collections are persistent current-catalogue queries over people, non-Places tags, location and taken time.
- Photo Details shows the original filename and canonical people without exposing private source paths.
- Returning from Photo Details restores the originating Smart Collections context and Photo Details uses a context-aware Back destination.
- A maintainer can add/remove photo-level person presence without creating or changing face evidence, and Smart Collections People filtering includes that presence.
- `Places/` is reserved for location data, each photo revision has at most one effective place, and more-specific replacement preserves audit history.
- Places are filtered through the Smart Collections Location dimension with ancestor/descendant semantics and are excluded from ordinary tags.
- Persisted GPS can be reverse geocoded through the configured GeoNames web-service API without downloading GeoNames database extracts or hydrating originals.
- Configured GeoNames enrichment runs automatically in the server process, resumes unattempted/failed/deferred work from SQLite and does not require a long-lived browser request.
- Automatic GeoNames pacing stays within the documented normal free-service budget without requiring the maintainer to tune request counts.
- Archive/local processing never waits for GeoNames; newly persisted GPS metadata becomes eligible independently.
- Manual place corrections override automatic GeoNames enrichment and provider failures never fabricate location data.
- Missing EXIF/GPS/tag/person/place data fails the relevant predicate rather than inventing metadata.
- The extended non-deferred M19 workflow passes the final maintainer verification pass.
