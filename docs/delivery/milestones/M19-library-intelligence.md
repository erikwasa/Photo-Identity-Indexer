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

Automatic visible-content tagging is intentionally deferred. [WI-0049](../work-items/WI-0049-visible-content-tagging-experiment.md) remains as a design/experiment note that can be revived later, but it is not part of M19 completion.

## Completion and verification

The original WI-0056/WI-0050 baseline was verified on 2026-08-16. The 2026-08-19 consolidated extension pass completed WI-0061, WI-0062, WI-0063, WI-0066, WI-0067 and WI-0068 and exposed the missing archive-to-metadata lifecycle step later implemented by WI-0072.

The final real-application verification completed on **2026-08-26**. The maintainer reported PASS for the remaining WI-0072, WI-0064 and WI-0065 checks, including representative JPEG/iPhone HEIC metadata, automatic GPS-to-GeoNames pickup without a manual browser batch, restart/resume, manual Place precedence, corrected GeoNames pacing and Sweden-local/else-English naming. The authoritative closure record is [M20-maintainer-verification-2026-08-26.md](M20-maintainer-verification-2026-08-26.md).

All M19 work items are therefore completed and the canonical milestone status is **completed**.

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

WI-0072 makes metadata inspection part of bounded archive advancement. The exact revision must already be local and hash-verified; metadata inspection never creates an independent hydration path. Before archive analysis, durable proxy generation or Photo Identity-managed release proceeds for a revision, the advancement path ensures metadata inspection is current. That means both revisions established directly from already-local files and revisions hydrated for source verification enter the same metadata contract.

The explicit `/api/photo-metadata/backfill` operation remains available for historical catalogue rows and repair/retry. WI-0078 later added extraction-contract versioning so legacy/stale rows can be revisited safely without deleting existing metadata. Both paths retain local-only/hash-verification rules and keep manual Places/people/tags outside metadata refresh persistence.

Query-critical capture time/GPS stay in the stable WI-0050 persistence contract. Richer camera/lens/exposure fields and a bounded sanitized raw metadata snapshot are stored separately and exposed through Photo Details without reopening the original during normal viewing.

## GeoNames enrichment

GeoNames is the selected M19 reverse-geocoding provider. Photo Identity uses the GeoNames web-service API rather than downloaded GeoNames database extracts.

Reverse geocoding operates only from GPS coordinates already persisted in SQLite and never opens or hydrates originals. Configuring a private GeoNames username is the explicit opt-in to external reverse geocoding. Once configured, the normal operating model is automatic: a server-side hosted service notices eligible persisted-GPS revisions, reuses durable cache/attempt state and continues until no eligible work remains.

GeoNames remains independent of archive analysis. WI-0072 ensures capture metadata is inspected while the exact revision is already local and verified; if GPS is present, the resulting database row becomes eligible for the background reverse-geocoding queue. Archive processing does not wait on external GeoNames response times or provider pacing.

The automatic normal request interval has a conservative **30000 ms default**, but it is **not a hard minimum**. An explicit supported non-negative override is honored. Lower-level provider pacing is reconciled with the automatic interval so Settings/diagnostics report the normal effective gate actually used. Provider quota/account/transport backoff remains authoritative and can delay requests longer than the configured normal interval.

The default language policy is **Sweden-local / elsewhere-English**. The provider is first queried with local naming; Swedish results are retained, while non-Swedish coordinates obtain/cache the English representation under the policy-aware cache contract. Manual Place and manual-clear actions remain authoritative and are never silently overwritten by automatic enrichment.

Operator documentation makes clear that latitude/longitude are sent to the external GeoNames service during automatic enrichment and includes required provider attribution.

## Navigation semantics

Photo Details preserves the context from which it was opened. In particular, opening a result from Smart Collections and returning through browser/mouse Back restores the selected collection or transient preview, filters and result page rather than resetting the workspace. Photo Details exposes a context-aware Back action rather than always returning to `/collections`.

## Verification evidence

M19 verification was intentionally layered across real catalogue use, live GeoNames behavior and automated repository checks. The final 2026-08-26 maintainer pass established the remaining real-media/provider evidence after the corrective M20 slices were merged. The post-PR-#205 `main` comprehensive workflow #1244 (`32528282922`) was green before the final manual pass.

Historical checklists remain in [M19-consolidated-verification.md](M19-consolidated-verification.md) and the 2026-08-19 review documents. Their older `PENDING`/`INCOMPLETE` markers describe the state at that time; [M20-maintainer-verification-2026-08-26.md](M20-maintainer-verification-2026-08-26.md) is authoritative for final lifecycle closure.

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
- GeoNames normal pacing is configurable with a 30000 ms default, effective pacing is operator-visible and provider-directed backoff can take precedence.
- Sweden uses local GeoNames naming while non-Swedish automatic results use English under the policy-aware cache contract.
- Archive/local processing never waits for GeoNames; newly persisted GPS metadata becomes eligible independently.
- Manual place corrections override automatic GeoNames enrichment and provider failures never fabricate location data.
- A person can be reversibly hidden from Smart Collection discovery without disappearing from identity review/maintenance or invalidating existing saved collections.
- A person-oriented representative portrait resolves from an explicit valid face or deterministic automatic fallback without changing identity evidence.
- The modern Smart Collection people selector supports incremental case-insensitive search, persistent selected people, hidden-selection compatibility, representative portraits and stable PersonId-based `all`/`any` semantics.
- Missing EXIF/GPS/tag/person/place data fails the relevant predicate rather than inventing metadata.
- Final consolidated maintainer verification passed on 2026-08-26.
