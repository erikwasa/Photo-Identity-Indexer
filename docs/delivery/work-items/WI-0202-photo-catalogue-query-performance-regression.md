---
id: WI-0202
title: Restore photo catalogue query performance after schema-v33 acceptance regression
milestone: M30
status_source: ../status/work-items.yaml
depends_on: []
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0202: Restore photo catalogue query performance after schema-v33 acceptance regression

## Priority and tracking

**High-priority production-scale photo regression**, detected during the maintained Windows verification for WI-0151 (video ingestion). Tracked in [GitHub issue #589](https://github.com/erikwasa/Photo-Identity-Indexer/issues/589). WI-0151 remains **in_review** and is not accepted until photo-only regression checks pass.

## Objective

Restore reliable, responsive photo Archive, person-filtered and other Smart Collection queries, and the complete Slideshow library against the existing schema-v33 PostgreSQL catalogue, without discarding or re-ingesting media or relaxing privacy/selection semantics.

## Observed failures (2026-10-18)

- `GET /api/archive/status` was observed cancelling after roughly 70–100 seconds (HTTP 499); this does **not** prove the exact cause of the cancellation.
- Seven specific saved Smart Collection filters repeatedly failed in `PostgresSmartCollectionQueryRepository.QueryAsync` after approximately 30 seconds (`NpgsqlException` wrapping a read `TimeoutException`), while other saved filters completed. Saved slideshow cover requests use `limit=1`, but repository `QueryAsync` first calculates an exact `COUNT(*)` for the filter.
- An interactive `POST /api/smart-collections/query` failed with the same 30-second exception. The slideshow collection definition endpoint, person lookup, photo thumbnails, video posters and other small requests remained responsive.
- While reproducing Archive then Slideshows, up to four PostgreSQL backends remained `active` simultaneously for 26–29 seconds and showed no reported wait event. Blank wait fields do not prove CPU saturation.
- **Candidate primary cause (not yet confirmed):** On the database servicing these connections, `pg_stat_user_tables` reported no `last_analyze` or `last_autoanalyze` for representative photo tables, and estimated `n_live_tup` was only eight for `assets`/`asset_revisions`. These are *statistics estimates*, not actual counts. Statistics may be missing/stale following restore or migration. The observed filter-specific pattern could also result from SQL shape, join cardinality or indexing.
- The operator had recently freed local disk space, but logs do not prove original-file loss or corruption.

Only sanitized aggregate counts and timing appear here; the raw application logs, personal folder names, real database name, person/revision IDs and credentials remain private.

## Scope and investigation order

1. Use [PostgreSQL photo query regression runbook](../../operations/postgres-photo-query-regression-wi0202.md) to confirm the **actual application-connected database**, backups and read-only planner/statistics evidence. Never select a database by a historical name or by the word `rehearsal`.
2. On a verified backed-up, quiet catalogue and with operator approval, refresh PostgreSQL **planner statistics only** for the related photo query tables and compare repeated request timings before/after. `ANALYZE` is not a data restore or a correction to missing originals.
3. If `ANALYZE` resolves the failure, document the missing-statistics root cause with before/after timings and add an appropriate, safe post-restore/schema-maintenance guard or operator check so future large restored catalogues are not used with misleading estimates.
4. If failures remain, capture estimated execution plans on an isolated restored representative dataset; separately measure Smart Collection count/page query phases, CTE/window-function costs, indexes and selected predicates. The Archive status aggregation/serial-folder workflow needs an independent timing breakdown.
5. Implement the smallest justified SQL/schema/index/API correction and focused regression tests. Bound per-cover work or expose partial progress only if it preserves correct membership/count semantics; do **not** just raise timeouts or hide failed collections.
6. Rerun the maintained Windows photo regression suite plus photo-only video-isolation checks. Collect privacy-safe count/timing and API result evidence.

## Safety

- No production reset, schema downgrade, forced rebuild, mass ingestion, OneDrive hydration, deletion of originals, `VACUUM FULL`, or destructive database maintenance.
- Do not commit private logs, SQL parameter values, full `pg_stat_activity.query` text, database names, source paths, faces, photos, GPS or credentials.
- Statistics refresh writes PostgreSQL planner metadata; it is **not read-only** and requires backup verification, a quiet window and operator sign-off. Diagnostic reads may run before that.
- Preserve photo-only default behavior, ordering, exact counts, person/tag/date/place filters, excluded revision privacy and all supported slideshow collections. Existing verified video behavior must remain intact.

## Out of scope

- General video processing enhancements, HDR previews, video playback or face recognition in videos.
- Wholesale replacement of Smart Collections or Archive architecture without evidence.
- Masking the regression by extending Npgsql timeouts indefinitely.

## Acceptance criteria

- [ ] Actual database identity, catalog scale and initial PostgreSQL statistics state are verified without guessing from its name.
- [ ] A controlled before/after diagnosis identifies or excludes missing statistics as the dominant contributor.
- [ ] Saved failing Smart Collection filters and ad hoc person-based queries return correct photos/counts without 30-second HTTP 500 timeouts, including repeated `limit=1` slideshow-cover requests.
- [ ] Archive status and first Archive page reliably complete on the maintained dataset without hanging/cancelled 70–100 second attempts; server-side phase timings are documented.
- [ ] Full Slideshow library shows all expected Smart/manual/Creative collections; no waiting for multiple sequential 30-second failed covers.
- [ ] Photo-only isolation, exclusions, sort/order and original-byte privacy invariants pass automated regression tests.
- [ ] Any repair is backed by before/after representative timings, a repeatable test or post-restore maintenance guard, and no destructive catalogue changes.
- [ ] Maintainer reruns Archive, saved and ad hoc person-filtered Smart Collections and Slideshows after packaging; WI-0151 remains in_review until signed off.

## Verification requirements

Focused automated Postgres/API tests and explicit maintained Windows acceptance with a backed-up production-scale catalogue. Test on an isolated restored copy before a costly or unsafe diagnostic. Do not publish unsanitized logs or plans.

## Completion notes

- Files changed:
- Diagnosed cause and ruled-out causes:
- Before/after evidence:
- Safety gates and commands run:
- Maintainer acceptance:
- Deferred work:
