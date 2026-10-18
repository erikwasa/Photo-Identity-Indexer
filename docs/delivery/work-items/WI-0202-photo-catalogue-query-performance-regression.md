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

## Maintained Windows recovery checkpoint — 2026-10-18

**Operator result: all five photo-only local checks passed after targeted `ANALYZE VERBOSE`.** Archive loads completely, a previously failing person-filtered Smart Collection works, an existing working collection still works, the Slideshow library/covers populate and Archive/Slideshows succeed again on repeat. These are maintainer-reported functional results, not isolated CI measurements.

Sanitized evidence from the operator's private PowerShell transcript and the corresponding packaged API log:

- The same PostgreSQL 18.6 / schema-v33 catalogue reported **27,076 actual assets and 27,076 actual asset revisions**. Before maintenance, `pg_class.reltuples` was about **27,068** for each—already near the true counts—while `pg_stat_user_tables.n_live_tup` was **8**, a cumulative monitoring estimate **not** the planner estimate. Existing `pg_stats` rows were present for most inspected relations; only the tiny `photo_tag_actions` table had no recorded column statistics. `autovacuum` and `track_counts` were both `on`.
- `ANALYZE VERBOSE` completed successfully for all **11** selected photo-query tables. PostgreSQL reported **27,076 live asset rows** and **27,076 live revision rows** during the refresh. No SQL/DDL application patch or catalogue restore was performed as part of this evidence.
- Health reported `status=ok`, `catalogueProvider=postgresql`, PostgreSQL `ready`, schema **33**.
- Archive status: **four HTTP 200** completions in the attached API log, approximately **0.91–1.09 s**; a separate repeated PowerShell request succeeded with HTTP 200 in **1.49 s**. First Archive page: **two HTTP 200** at roughly **188 ms**.
- Saved Smart slideshow cover filters: **14/14 HTTP 200** in the attached log, including **all seven previously timing-out filter definitions**. Median approximately **246 ms**; three exceeded one second and the slowest took **10.42 s**. An ad hoc person-based Smart Collection query returned HTTP 200 in about **332 ms**.
- The submitted post-maintenance log contains **no HTTP 499/500 or recorded application exceptions**. The five reported UI verification items all passed, including a second Archive/Slideshow visit.

**Causal inference limits:** the recovery followed `ANALYZE`, and it is the only recorded database maintenance trial in the supplied output, making planner-statistics refresh a strong operational recovery candidate. However, the pre-maintenance `reltuples` estimates were already close and usable `pg_stats` rows existed, so claiming that all tables had missing or grossly stale *planner* estimates would be incorrect. No pre/post SQL plans were saved; the precise plan change and whether the incident was caused by reset activity counters, different filter cardinality or another planner statistic cannot be proven from these logs. The supplied transcript does **not** include the backup/restore-verification result or post-`ANALYZE` stats snapshot; do not claim either was checked.

**Disposition:** restored in the maintained environment; progress to **in_review**, not terminal completion. Preserve this [safe manual recovery procedure](../../operations/postgres-photo-query-regression-wi0202.md) and do not add unconditional startup `ANALYZE` or increase query timeouts on this evidence alone. Review outstanding backup evidence and the intermittent **10.42 s** saved cover latency, and repeat after a future independent cold start or representative restore before deciding final completion or a narrower performance follow-up. WI-0151 photo-only blocker symptoms are cleared by local verification, but that does not certify any unrelated WI-0151 acceptance gates.

## Query optimization and derived face-work storage investigation — 2026-10-18

A separate maintained-host size report identified a PostgreSQL catalogue of approximately **18 GB**. The largest table, provisional_face_cluster_members, accounts for **~16 GB** (6,261 MB heap, 10,045 MB indexes); identity_match_regeneration_targets accounts for **~1,086 MB** (335 MB heap, 751 MB indexes). Several older restored databases are approximately **13 GB**, but backup timestamps and equal row populations are not established by this size report. The main database is expected to be renamed; do not hard-code either name.

**Repo code findings:**
- Both large tables are **derived face-work persistence**, not inputs to the Smart Collection query. Provisional cluster publication supersedes a previous run without deleting historical member rows; regeneration starts snapshot targets and retains each run's target rows. Valid retention/history obligations and physical bloat have not yet been measured. Big indexes are not automatically redundant.
- Smart Collection paging runs exact COUNT(*) before page SELECT even for Slideshow limit=1 cover requests; shared latest-action window CTEs and date/people filters are potential performance costs. We do **not** have phase timings or plan evidence proving an inefficient SQL plan. A rare post-recovery ~10.42-second cover remains under investigation.
- Targeted SQL optimization (conditional joins in the count only, query-plan/index tuning or retention) must be preceded by **read-only measurements** and proven result semantics on a representative restored catalogue.

**Next diagnostic checkpoint:** execute [read-only storage/retention and cover-timing runbook](../../operations/postgres-wi0202-query-and-index-audit.md). It adds a bounded, read-only SQL metadata audit and a sequential, privacy-safe HTTP cover probe, both operator-run. Collect **aggregate** index sizes, per-status run counts/target totals, dead/live estimates and two passes of saved-cover latency, including after a cold start if possible. Only then choose a safe scoped optimization/retention proposal. No production DDL, SQL index rebuild, run pruning, startup maintenance or data migration is included in this checkpoint.

WI-0202 stays in review with symptoms restored; the new work is investigative, not a claim that the 10 GB index growth caused Smart Collection timeouts.


## Historical clustering run accumulation and diagnostic bug — 2026-10-18

The maintainer ran the read-only WI-0202 storage audit against the actual app-connected PostgreSQL 18.6 catalogue. Aggregate findings: **90,160,544 estimated provisional cluster member rows**, ~**16 GB** including ~**10.5 billion index bytes**; **4,406,195 estimated regeneration target rows** and ~**1.14 billion bytes** total. Of **13,802** provisional clustering runs, **12,212** are superseded, **1,561** stale, **27** failed and just **2** completed/current. The superseded runs alone declare **95,461,882 target rows** across many historical runs, while the two current pinned runs declare **10,913**. These are declared totals and planner estimates, not exact historical member counts.

The growth is **consistent with retained derived run history**, not proof of B-tree index bloat. Main-vs-restore database byte differences remain inconclusive without equal table row populations/backup timing. Observed zero index scans and insert counters in PostgreSQL cumulative views are *not* enough to remove indexes or infer inactivity.

Repository audit also found provisional_face_not_same_constraints.source_run_id has **ON DELETE SET NULL** behavior. Wholesale deletion of historical cluster runs would remove some provenances even if the persisted not-same-person constraints remain. Future retention must explicitly preserve auditability and current run linkage, be dry-run-first, and be tested on a restored database. No deletion, index rebuild, schema change or maintenance is authorized by this checkpoint.

The first local Smart cover probe failed because the API was stopped; a second run exposed a **diagnostic PowerShell JSON-array enumeration bug**, not an invalid saved collection. This PR fixes probe parsing; post-fix cover timing results are pending from the maintainer. Continue investigating SQL count/page phase latency separately from face-work table growth.
