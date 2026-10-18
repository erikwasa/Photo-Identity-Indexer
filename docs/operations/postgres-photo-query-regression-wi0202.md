# WI-0202 — PostgreSQL photo query regression: safe triage and statistics recovery

**Issue:** [#589](https://github.com/erikwasa/Photo-Identity-Indexer/issues/589) · **Work item:** [WI-0202](../delivery/work-items/WI-0202-photo-catalogue-query-performance-regression.md) · **Blocks:** WI-0151 maintained photo-only acceptance.

## Symptom and current hypothesis

Maintained schema-v33 application: Archive status does not reliably complete (HTTP 499 after ~70–100 seconds), Smart Collection SQL times out around 30 seconds for some filters, and Slide Library waits for multiple cover queries. Four concurrent PostgreSQL sessions were observed `active` for ~29 seconds with empty wait-event fields. Small reads and photo/video thumbnails succeed.

`pg_stat_user_tables` for the database serving those requests reported `last_analyze` and `last_autoanalyze` empty for representative photo tables; `n_live_tup` estimated eight entries for `assets` and `asset_revisions`. These estimates **do not represent reliable actual row counts without current planner statistics**. A restore may omit planner stats; whether that explains this specific failure remains to be proven.

**Do not** treat a historical timestamp/rehearsal-like database name as proof that it is disposable, change the launcher to a different database, drop/recreate anything, restore over the active DB, run `VACUUM FULL`, bulk hydrate OneDrive files or merely increase the timeout.

## A. Preflight: no writes

1. Keep the existing maintained backup and restore-verification report. Confirm the backup corresponds to the **active** catalogue. If uncertain, stop before section C.
2. Prefer to close browser tabs/long archive queries when measuring an idle baseline. Verify the actual target DB from the launcher's active connection settings and/or the `datname` seen on currently running Photo Identity queries. Never infer it from the name.
3. From PowerShell, choose the existing running PostgreSQL container (`$container` from the verified `podman compose ps -q postgres`) and replace `<VERIFIED_DB>` in each command below with the database just confirmed. **Do not post its name or the full outputs publicly.**

Read-only identity/connection check:

```powershell
podman exec $container sh -lc 'psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "<VERIFIED_DB>" -c "SELECT current_database(), version();"'
```

Read-only statistics and relative table sizes (bounded output; **estimated** row counts, not exact totals):

```powershell
podman exec $container sh -lc 'psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "<VERIFIED_DB>" -c "SELECT relname, n_live_tup, n_mod_since_analyze, last_analyze, last_autoanalyze, pg_size_pretty(pg_total_relation_size(relid)) AS total_size FROM pg_stat_user_tables ORDER BY pg_total_relation_size(relid) DESC LIMIT 30;"'
```

Read-only autovacuum/metrics configuration:

```powershell
podman exec $container sh -lc 'psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "<VERIFIED_DB>" -c "SHOW autovacuum;"'
```

Observe only activity *metadata* during a single problematic request; do not expose SQL text or query parameters:

```powershell
podman exec $container sh -lc 'psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "<VERIFIED_DB>" -c "SELECT pid, state, wait_event_type, wait_event, now() - query_start AS duration FROM pg_stat_activity WHERE datname = current_database() AND pid <> pg_backend_pid() ORDER BY query_start;"'
```

Blank `wait_event` is *not* proof that all CPU or I/O time has been explained.

## B. Record baseline

Run Archive and Slideshow navigation **one at a time**. Capture, privately, a bounded baseline: Archive status completion/cancellation and elapsed time; complete Slideshows load; known fast and known slow saved Smart Collection filter response and elapsed time; ad hoc person-filter query response/elapsed time; any HTTP 500/499. Keep exact person/photo IDs and raw API logs local. Record the number of included roots / Smart Collections only as aggregate counts.

No `ANALYZE` is required yet. At this point the evidence can still implicate a wrong target database, stale statistics, inefficient joins or separate Archive behavior.

## C. Controlled statistics-refresh trial — writes **only to planner statistics**

**Only after** confirming the active DB, a valid backed-up and restorable catalogue, and a quiet time window: with maintainer approval, try *targeted* PostgreSQL statistics maintenance. `ANALYZE` samples relevant tables, updates catalog planner statistics and incurs disk/CPU load and a lightweight maintenance lock; it does not alter photo identities, original media or source contents.

```powershell
podman exec $container sh -lc 'psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "<VERIFIED_DB>" -c "ANALYZE VERBOSE assets, asset_revisions, face_occurrences, review_actions, people, photo_person_actions, photo_tag_actions, photo_tags, photo_place_actions, photo_capture_date_actions, photo_capture_metadata;"'
```

Do not substitute `VACUUM FULL`, `REINDEX`, `TRUNCATE`, schema initialization or a restore for this trial. If any table does not exist in the verified database, stop and review its schema first rather than modifying table definitions. Avoid opening several heavy UI pages during the maintenance.

Re-run the **same** read-only stats query and the **same** baseline requests in section B. Record before/after time, success codes and count/result consistency. An updated `last_analyze` establishes that stats were refreshed, **not** by itself that they explain the regression. Archive status and Smart Collection SQL may have independent causes.

## D. If the trial does not resolve all symptoms

Use an isolated representative restored DB for further experiments. Investigate:

- Smart Collection SQL `QueryAsync` separately for exact count and page retrieval: the `limit=1` cover route still performs the full predicate `COUNT(*)`; the shared CTEs include latest-action `ROW_NUMBER()` windows. Compare fast vs slow filters and current indexes/cardinality.
- `EXPLAIN (FORMAT JSON)` **without** `ANALYZE` for a sanitized or private parameterized query, on the isolated copy. `EXPLAIN ANALYZE` actually executes SQL and must not be run unbounded on the maintained catalogue.
- Archive `/api/archive/status`: instrument per-included-folder query latency, count/aggregation work, and cancellation behavior independently from Smart Collection failures. Distinguish HTTP 499 client cancellation from SQL timeout.
- After diagnosis, implement a scoped SQL/index/phase or safe post-restore statistics guard with focused correctness tests. Preserve photo-only media-kind filters, exclusions, page counts/order, person/tag/place/date semantics and default-off video isolation.

## E. Maintainer sign-off

Required before WI-0202 and WI-0151 can be accepted: Archive fully loads; saved fast+previously failing Smart Collection filters and ad hoc people-based query return correct photos/counts; full Slideshow library and covers load without the observed timeouts; repeat once after a new app start. Save only privacy-safe timings/counts and test outcomes in the work item.

This runbook **records a hypothesis and trial**, not a completed fix or authorization to modify the production database.
