---
id: M19
title: Photo metadata and semantic collections
status_source: ../status/milestones.yaml
depends_on: [M12, M14]
---

# M19: Photo metadata and semantic collections

## Outcome

The catalogue can organize photos using photographic capture metadata, location, people and maintainer-owned hierarchical tags. Saved smart collections combine these dimensions and reevaluate against the current catalogue so newly ingested matching photos appear automatically.

The extended M19 scope also makes Photo Details a useful catalogue-inspection surface, supports manual photo-level people when no face was detected, treats `Places/` as a first-class single-valued location hierarchy, enriches GPS-bearing photos through the GeoNames web-service API, keeps that enrichment running automatically in the server process rather than requiring repeated browser batches, and improves person-oriented Smart Collection discovery with visibility preferences, representative portraits and searchable multi-selection.

## Work items

- [WI-0056](../work-items/WI-0056-manual-photo-tags.md) — make manual photo tags hierarchical and compatible with Immich tag-path semantics while retaining SQLite as the source of truth
- [WI-0050](../work-items/WI-0050-exif-smart-collections.md) — ingest capture metadata and persist reusable smart collections over people, tags, location and taken-time filters
- [WI-0061](../work-items/WI-0061-photo-details-navigation-context.md) — show privacy-safe filename/people details and preserve the originating collection/navigation context
- [WI-0062](../work-items/WI-0062-manual-photo-people.md) — add revision-level manual person presence without fabricating face evidence
- [WI-0063](../work-items/WI-0063-first-class-places.md) — reserve `Places/`, enforce one effective place per revision and move named places into the Smart Collections Location dimension
- [WI-0064](../work-items/WI-0064-geonames-place-enrichment.md) — reverse geocode persisted GPS coordinates through the GeoNames API into canonical Places hierarchy
- [WI-0065](../work-items/WI-0065-automatic-place-enrichment.md) — continuously drain the persisted GeoNames queue in a provider-safe server-side worker so new/retryable GPS photos are enriched without a manual browser batch
- [WI-0066](../work-items/WI-0066-smart-collection-person-visibility.md) — allow people to be hidden from normal Smart Collection discovery without weakening identity evidence or existing saved definitions
- [WI-0067](../work-items/WI-0067-featured-person-face.md) — provide explicit or deterministic automatic representative face portraits for person-oriented UI
- [WI-0068](../work-items/WI-0068-searchable-smart-collection-people.md) — replace the long people checkbox list with searchable portrait-led multi-selection while preserving PersonId and `all`/`any` semantics
- [WI-0072](../work-items/WI-0072-archive-photo-metadata.md) — integrate safe metadata inspection into archive advancement, expand persisted camera/exposure metadata and expose key plus raw metadata in Photo Details

Automatic visible-content tagging is intentionally deferred. [WI-0049](../work-items/WI-0049-visible-content-tagging-experiment.md) remains as a design/experiment note that can be revived later, but it is not part of the active M19 completion path.

## Verified baseline

WI-0056 and WI-0050 established the original M19 baseline: hierarchical manual tags, capture-time/GPS persistence, safe metadata backfill and persistent smart collections over people/tags/GPS/taken time. The maintainer completed the integrated baseline verification on 2026-08-16 and reported that M19 and its work-item functions behaved as expected.

The 2026-08-19 consolidated extension pass completed WI-0061, WI-0062, WI-0063, WI-0066, WI-0067 and WI-0068. WI-0064/WI-0065 remain in review because that pass exposed that newly archived revisions could finish analysis without capture metadata being inspected, leaving GPS-bearing photos outside the automatic GeoNames queue. WI-0072 owns that ingestion and Photo Details metadata follow-up.

## Tag and Places architecture

General manual tags follow Immich-style hierarchy semantics: a tag has a local `name`, a full-path `value`, an optional parent and an optional color. `/` is the hierarchy separator.

The extended scope reserves `Places` as a special root for location data. `Places/Sweden/Stockholm region/Norrtälje` is a canonical location hierarchy, but a photo has only one effective place assignment. Parent nodes remain reusable vocabulary rather than separate active assignments.

Places are not ordinary Smart Collection tags. They belong to the Location dimension, where selecting an ancestor such as Sweden matches descendant assignments. Normal UI does not need to show the literal `Places/` prefix.

Photo Identity continues to persist metadata and revision-bound actions in SQLite. It does not adopt XMP sidecar write-back. Originals remain read-only and metadata-only edits must not hydrate online-only files.

## People architecture

Confirmed face assignments remain face evidence. M19 also records that a canonical person is present in a photo even when no usable face was detected.

Photo-level manual person presence is revision-bound catalogue metadata and remains separate from face occurrences, crops, embeddings, review assignments and identity suggestions. Smart Collections use the union of confirmed face people and active manual photo-level people, while face-review/evidence workflows remain face-based.

Person presentation metadata is also kept separate from identity evidence. A person can be hidden from normal Smart Collection discovery without disappearing from review/maintenance, and can have an explicit representative face with deterministic fallback. Existing saved collections that reference a hidden person remain valid until the maintainer removes that selection.

The Smart Collection people editor stores canonical PersonIds, not display names. Search and representative portraits are presentation aids only; person renames therefore do not invalidate saved definitions and `all`/`any` semantics remain unchanged.

## Smart-collection semantics

A smart collection is a persisted query definition, not a copied list of asset IDs. Results are evaluated from the current catalogue each time the collection is opened or queried, so new matching photos join automatically.

The definition can combine:

- people — one or more canonical people, with `all` or `any` matching inside the people dimension;
- tags — one or more non-Places canonical full tag values, with `all` or `any` matching inside the tag dimension;
- location — an optional canonical named place and/or optional GPS coordinate bounds;
- taken time — inclusive photographic date ranges derived from capture metadata.

Different dimensions combine with AND semantics. Named-place matching is hierarchical: selecting `Places/Sweden` matches that node and descendant places such as `Places/Sweden/Stockholm region/Norrtälje`.

## Capture metadata lifecycle

Capture metadata remains revision-bound. `DateTimeOriginal` is photographic wall-clock time and must not be converted to UTC when the source has no offset. A real source offset is persisted separately. GPS latitude/longitude remain atomic.

WI-0072 makes metadata inspection part of bounded archive advancement. The exact revision must already be local and hash-verified; metadata inspection never creates an independent hydration path. Before archive analysis, durable proxy generation or Photo Identity-managed release proceeds for a revision, the advancement path ensures a capture-metadata inspection row exists. That means both revisions established directly from already-local files and revisions hydrated for source verification enter the same metadata contract.

The explicit `/api/photo-metadata/backfill` operation remains available for historical catalogue rows and repair/retry. It reuses the same inspection/persistence boundary and retains its local-only/hash-verification rules.

Query-critical capture time/GPS stay in the stable WI-0050 persistence contract. Richer camera/lens/exposure fields and a bounded sanitized raw metadata snapshot are stored separately and exposed through Photo Details without reopening the original during normal viewing.

## GeoNames enrichment

GeoNames is the selected M19 reverse-geocoding provider. Photo Identity uses the GeoNames web-service API rather than downloaded GeoNames database extracts.

Reverse geocoding operates only from GPS coordinates already persisted in SQLite and never opens or hydrates originals. Configuring a private GeoNames username is the explicit opt-in to external reverse geocoding. Once configured, the normal operating model is automatic: a server-side hosted service notices eligible persisted-GPS revisions, reuses the existing WI-0064 cache/attempt state and continues until no eligible work remains.

GeoNames remains independent of archive analysis. WI-0072 ensures capture metadata is inspected while the exact revision is already local and verified; if GPS is present, the resulting database row becomes eligible for the existing background reverse-geocoding queue. Archive processing does not wait on external GeoNames response times or provider pacing.

Automatic provider pacing must be conservative enough that the maintainer does not need to calculate free-service limits. As of 2026-08-18 GeoNames documents 10,000 credits/day and 1,000 credits/hour per username/application, and `findNearbyPlaceName` costs 3 credits per request. WI-0065 therefore applies a 30-second automatic request floor, while provider quota/overload/transport responses pause and retry automatically. Small manual maintenance/force-refresh actions remain available for diagnostics only.

Manual place corrections take precedence over automatic GeoNames results. Operator documentation must make clear that latitude/longitude are sent to the external GeoNames service during automatic enrichment and must include required provider attribution.

## Navigation semantics

Photo Details should preserve the context from which it was opened. In particular, opening a result from Smart Collections and returning through browser/mouse Back must restore the selected collection or transient preview, filters and result page rather than resetting the workspace. Photo Details should expose a context-aware Back action instead of always returning to `/collections`.

## Verification strategy

The original M19 baseline was verified on 2026-08-16 and six later extension work items passed the consolidated maintainer check on 2026-08-19. WI-0064 live-provider verification already established real GeoNames normalization, Smart Collection integration and long hierarchy storage; WI-0065 verification still requires automatic pickup/restart-resume on newly processed GPS-bearing data.

WI-0072 is the active M19 completion path. Automated coverage should verify shared metadata persistence, no metadata-only hydration, Photo Details contracts and archive gating. Final maintainer verification should use at least one date/GPS-bearing JPEG and one representative iPhone HEIC/HEIF image so the real media metadata and automatic GeoNames path are exercised before WI-0064/WI-0065/WI-0072 and M19 are completed.

Do not mark WI-0072 completed merely because implementation and CI are green. Keep it `in_review` until the real archive/metadata/GeoNames verification passes.

## Exit criteria

- Manual non-Places tags support hierarchical Immich-compatible values while remaining SQLite-backed and revision-audited.
- Capture time is stored with photographic local-time semantics instead of inventing UTC when the source metadata has no offset.
- GPS metadata is retained when available without making location mandatory.
- Newly archived exact revisions are metadata-inspected automatically while already local and hash-verified, without a metadata-only hydration path.
- Photo Details exposes inspection state, useful camera/capture/exposure/GPS fields and bounded sanitized raw metadata from SQLite without reopening the original for rendering.
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
- A person can be reversibly hidden from Smart Collection discovery without disappearing from identity review/maintenance or invalidating existing saved collections.
- A person-oriented representative portrait resolves from an explicit valid face or deterministic automatic fallback without changing identity evidence.
- The modern Smart Collection people selector supports incremental case-insensitive search, persistent selected people, hidden-selection compatibility, representative portraits and stable PersonId-based `all`/`any` semantics.
- Missing EXIF/GPS/tag/person/place data fails the relevant predicate rather than inventing metadata.
- The extended non-deferred M19 workflow passes final maintainer verification.
