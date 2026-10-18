# WI-0202 — read-only Smart Collection and oversized-index investigation

**Issue:** [#589](https://github.com/erikwasa/Photo-Identity-Indexer/issues/589) · **Work item:** [WI-0202](../delivery/work-items/WI-0202-photo-catalogue-query-performance-regression.md)

This procedure separates two different concerns: slow Smart Collection covers, and unusually large historical face-clustering / identity-regeneration tables. The photo regression recovered after targeted PostgreSQL ANALYZE; an individual saved Smart cover still took approximately 10.42 seconds. Neither that tail nor the table sizes establishes a specific SQL plan or an index defect.

## Code audit

- [Smart Collection query repository](../../src/PhotoIdentity.Persistence.Postgres/PostgresSmartCollectionQueryRepository.cs): each paged request (including a Slideshow cover with limit=1) runs an exact COUNT(*) before querying the requested page. The SQL contains latest-action window-function CTEs for person, tag, place and date semantics. The page query also sorts by effective capture date. We do not yet know whether slow covers spend time in the count, page or both.
- [Provisional cluster repository](../../src/PhotoIdentity.Persistence.Postgres/PostgresProvisionalFaceClusterRepository.cs): completing a run replaces the current pointer, marks the former current run superseded, and retains that former run's member rows. Members have a composite primary key and a separate partial grouping index. Retention may be intentional; no automatic pruning was found in this path.
- [Identity regeneration repository](../../src/PhotoIdentity.Persistence.Postgres/PostgresIdentityMatchRegenerationRepository.cs): every run snapshots eligible targets, and targets are retained with the run. They have primary/unique indexes and foreign keys. They are not directly joined by Smart Collection SQL.
- The main (~18 GB) vs older restored (~13 GB) database-size difference may reflect later writes or physical index/table bloat. Total-size comparison alone proves neither.

## A. Select the current app-connected database

From the repo root in PowerShell, after this PR merges:

~~~powershell
git pull --ff-only
Push-Location .\deploy\postgres
try {
    $container = (podman compose ps -q postgres).Trim()
}
finally {
    Pop-Location
}
if (-not $container) { throw 'No running PostgreSQL compose container.' }

$db = Read-Host 'Enter confirmed app-connected PostgreSQL database'
if ($db -notmatch '^[a-zA-Z_][a-zA-Z_0-9]*$') {
    throw 'Unexpected database name format.'
}
~~~

Confirm the database via the application's active launcher settings. It may soon be renamed to photoidentity_main: use the actual connected name at execution time. A rehearsal-like name is not evidence that a database is disposable.

## B. Run the bounded storage/retention/index audit

~~~powershell
Get-Content -LiteralPath '.\tools\PhotoIdentity.PostgresDiagnostics\wi0202-readonly-storage-audit.sql' -Raw |
    podman exec -i -e "TARGET_DATABASE=$db" $container sh -lc 'psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$TARGET_DATABASE" -f -'
if ($LASTEXITCODE -ne 0) { throw 'Read-only storage audit failed.' }
~~~

This uses a READ ONLY transaction and a per-statement 15-second timeout. It reports physical table and index sizes, planner/live/dead estimates, vacuum/analyze times, per-index size and scan counts, status-grouped **run metadata** and currently pinned clustering scopes. It deliberately does not run an exact COUNT(*) or GROUP BY on the enormous membership/target tables, install extensions, emit real IDs, or modify user data. Values such as reltuples, n_live_tup and n_dead_tup are **estimates**, and idx_scan is a cumulative counter, not proof that an index can be dropped.

If an earlier restored database is retained and independently verified, you may run the exact same audit against it by changing only the $db variable. Compare specific table and index sizes with row-count estimates and backup timing. A restored copy may be smaller because the dump was earlier, or because the restore rebuilt fragmented relations. Never assume it has equal data based on a schema version or database name.

## C. Measure every saved Smart Collection cover, safely

With the normally launched API on localhost:

~~~powershell
.\tools\PhotoIdentity.PostgresDiagnostics\measure-smart-cover-latency.ps1 -BaseUrl 'http://127.0.0.1:5080' -Repetitions 2 -TimeoutSeconds 35
~~~

The tool sequentially requests limit=1 for each saved Smart Collection and prints only anonymous ordinal, HTTP status and elapsed milliseconds. It does not expose collection IDs, filter details or response bodies. Repeat after a normal independent app restart if practical. Note whether the same ordinals consistently exceed 3–10 seconds; ordinals remain meaningful only while saved collection definitions are unchanged.

## D. Interpretation and next steps

| Evidence | Next action |
| --- | --- |
| Old superseded/completed run target totals greatly exceed current pinned target totals | Design **explicit retention** on an isolated restored copy, considering history, current pointers, identity review evidence and recoverability. No cleanup authorization is implied. |
| Large estimated dead-row counts and disproportionately large indexes even against a same-row-count restored database | Investigate autovacuum and real index bloat; separately approve any maintenance. Index-to-heap ratio by itself is not bloat proof. |
| Specific saved cover ordinals consistently take several seconds | Time count and page phases separately, then capture private parameterized estimated EXPLAIN (FORMAT JSON) plans on an isolated restored catalogue. Preserve exact result counts, ordering and exclusions. |
| All Smart covers remain quick and the derived tables consist of deliberately retained runs | Keep the documented manual statistics recovery; consider a separately scoped storage retention work item. |

Potential optimization **not yet shipped**: for a Smart filter without date or age, its COUNT(*) may not need the effective capture-date join; without location, it may not need the capture-metadata outer join. The page query still requires capture-date ordering and projected metadata. Verify actual uniqueness constraints, count semantics, real plans and focused regression tests before implementing conditional count SQL. Do not substitute approximate counts, increase timeouts or drop large indexes speculatively.

**Safety:** No TRUNCATE, DELETE, DROP, VACUUM FULL, REINDEX, database rename, forced restore, or uncontrolled EXPLAIN ANALYZE against the maintained catalogue. Keep actual database names, SQL parameters, IDs, credentials and private full API logs outside the repo.
