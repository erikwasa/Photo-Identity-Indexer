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


## Maintainer read-only results and probe correction (2026-10-18)

**Confirmed PostgreSQL 18.6 / 18,820,085,439-byte (~18 GB) database.** The following are **planner estimates and run-metadata totals**, not exact member-row counts:

- provisional_face_cluster_members: **17,099,743,232 bytes**; heap **6,565,183,488**, indexes **10,532,716,544**, planner estimate **90,160,544 rows**. The primary-key index alone is **8,546,107,392 bytes** and the partial grouping index **1,984,995,328 bytes**. These indexes are required by current constraints/query patterns unless a verified replacement is designed.
- identity_match_regeneration_targets: **1,138,999,296 bytes**; heap **351,535,104**, indexes **787,333,120**, planner estimate **4,406,195 rows**. Both existing indexes enforce primary/unique constraints.
- Provisional clustering control rows: **13,802 runs**: 12,212 superseded, 1,561 stale, 27 failed, 2 completed. Superseded runs declare a combined **95,461,882 targets**; the **two current pinned completed runs** declare only **10,913 targets**. A run is capped at 20,000 faces; many historical runs readily explain a large accumulated member population without any necessary index bloat.
- Identity regeneration controls: **1,120 runs**: 944 stale, 159 completed and 17 failed. Their declared targets total approximately 4.9 million (including 4,550,085 from stale runs). They are *not* directly joined by the Smart Collection query.
- For both large derived tables, cumulative n_tup_ins/n_tup_upd/n_tup_del and observed index scans were **zero**, and vacuum/analyze timestamps blank. The session statistics can have been reset or may not reflect earlier work; **zero scans does not imply unused indexes**, nor does blank analyze prove an empty table.

**Deletion/provenance hazard:** The code for provisional_face_not_same_constraints stores a nullable source_run_id referencing provisional_face_cluster_runs with **ON DELETE SET NULL**. A run-deletion retention policy could silently erase provenance for still-relevant not-same constraints even while preserving the constraint itself. Run-level history may also matter for audit or reproducibility. Future pruning must account for that relationship and current pointers, preserve committed review decisions, be dry-run-first and operate against a verified isolated restore before requesting maintenance approval.

**Probe bug discovered:** One trial failed to connect because the Photo Identity API was not running at localhost:5080; subsequent trial reached the API but the probe misinterpreted an Invoke-RestMethod JSON array as one nested definition, yielding "invalid identifier". The corrected script materializes the response, explicitly enumerates definitions and checks that each ID parses as a GUID. This does **not** indicate bad saved collections.

Next: rerun the corrected probe twice while /health reports 200. Only after seeing whether slow ordinals recur should we instrument Smart Collection count/page phases or propose query-plan changes.


## Next query experiment: conditional count joins and slow-phase logging

Maintainer baseline from two sequential passes, **28/28 HTTP 200**: ordinal #7 took 9,718/9,811 ms; #13 took 2,067/2,089 ms; #14 took 2,113/2,141 ms. Other 11 collection cover queries were at most 467 ms. These are stable by ordinal only while collection definitions do not change.

The next PR conditionally omits unused count-only metadata/date joins while keeping exact COUNT(*), unchanged page SELECT, photo media-kind and all filter semantics. It also records private-safe count/page phase warnings if either phase lasts at least one second. **This is a testable optimization hypothesis**, not proof that it accounts for the slow #7 query or that index changes are necessary.

After CI passes and the PR merges, package/start Photo Identity **as usual** (code changes will not affect an already-running packaged binary) and run:

~~~powershell
git pull --ff-only
.\tools\PhotoIdentity.PostgresDiagnostics\measure-smart-cover-latency.ps1 `
    -BaseUrl 'http://127.0.0.1:5080' `
    -Repetitions 2 `
    -TimeoutSeconds 35
~~~

The probe now prints safe **FilterDimensions** labels by ordinal without the underlying values. For any slow result, compare the local API console/log entries beginning with "WI-0202 slow Smart Collection count phase" or "WI-0202 slow Smart Collection page phase". These log only per-phase milliseconds and filter presence flags, not identities. Share only those sanitized timing lines and the sanitized two-pass table; keep full logs private.

If the slow time comes mostly from **count**, next isolate that filter's COUNT plan. If mostly **page**, focus on sort/projection and the effective date/person/place CTEs. In either case use a verified isolated restored database for parameterized estimated EXPLAIN, and preserve the exact count/filter/ordering contract.


## Post-#594 isolated phases and next selective SQL trial

Maintainer's second two-pass baseline on the packaged PostgreSQL app: #7 people+relationship **5,965/6,061 ms** (logged page phase **4,986/5,123 ms**); #13 named-place+orientation **2,096/2,090 ms** (logged count **1,982/1,980 ms**); #14 named-place+orientation **2,262/2,150 ms** (logged count **2,148/2,090 ms**). Other covers stayed around 111–267 ms. All **28/28** returned HTTP 200.

The next narrow experiment changes only two SQL cases: (a) relationship-filtered paged queries first select the ordered LIMIT/OFFSET revisions in a MATERIALIZED CTE and then hydrate the selected people arrays and place; (b) named-place+orientation exact counts use a LATERAL lookup of the latest review proxy per asset revision and the identical proxy-first-or-catalogue geometry comparison. Existing photo-kind, exclusion, date, ordering, people, place, any/all and null geometry semantics must remain stable. Existing unaffected filters retain their original SQL paths.

After PR merge, rebuild/restart the packaged API, then rerun the same anonymous probe:

~~~powershell
git pull --ff-only
.\Package-PhotoIdentity.ps1 -Configuration Release
# Start the application with your normal launcher, then:
.\tools\PhotoIdentity.PostgresDiagnostics\measure-smart-cover-latency.ps1 -BaseUrl 'http://127.0.0.1:5080' -Repetitions 2 -TimeoutSeconds 35
~~~

Compare **#7/#13/#14** against both baselines and share only the sanitized probe table and any new "WI-0202 slow Smart Collection" phase lines. No maintenance on the unrelated ~10 GB historical face-clustering indexes is needed for this SQL experiment.


## Post-PR #595 acceptance and narrow people+relationship experiment — 2026-10-19

A two-trial maintained Windows run returned all **28/28 HTTP 200**. #13/#14 named-place+orientation covers dropped from approximately 2.1–2.2 seconds to **130–147 ms** (a ~93–94% improvement). #7 people+relationship remained **5,967/6,132 ms**, essentially unchanged from its earlier ~6-second result; all other covers remained responsive.

The next branch touches only the remaining slow shape: pages having both selected people and relationship constraints. It computes the set of related people first and intersects that eligibility with the requested person match in a single grouped revision_people pass; results are then passed through the unchanged ordered photo-only page selection and unchanged projection. It maintains exact count, any/all person membership, relationship kinds/direction, photo-only guards, stable date order, and pagination. No SQL migration, DDL, new indexes or destructive retention action is involved.

After CI passes and the PR merges, repackage, restart the normal Photo Identity launcher and repeat:

~~~powershell
git pull --ff-only
.\Package-PhotoIdentity.ps1 -Configuration Release
# Start Photo Identity normally, then:
.\tools\PhotoIdentity.PostgresDiagnostics\measure-smart-cover-latency.ps1 -BaseUrl 'http://127.0.0.1:5080' -Repetitions 2 -TimeoutSeconds 35
~~~

Share only anonymous ordinal, filter-dimensions, elapsed-ms table and any sanitized WI-0202 slow count/page warnings. If #7 remains near six seconds, use the existing phase timings plus a **controlled representative restore** to capture a safe, parameterized PostgreSQL plan; avoid further unmeasured index/SQL changes.


## Final post-#596 query performance checkpoint — 2026-10-19

On a refreshed packaged build, the maintainer's two sequential saved-cover passes completed **28/28 HTTP 200**. Ordinal #7 (people+relationship) took **1,029 / 1,078 ms**, mean **1,054 ms**: ~83% faster than pre-#596 and ~89% faster than the original ~9.8-second baseline. Ordinal #13 (named place+orientation) took **137 / 143 ms**; #14 took **145 / 134 ms**. All other eleven saved covers were within **111–279 ms**. The associated #596 CI workflow completed successfully.

These measurements validate a substantial improvement in the maintained workload without schema changes, approximate counts, data cleanup, destructive maintenance, index rebuilds, raised timeouts or new sensitive telemetry. They do not replace UI correctness, concurrency or cold-start tests. Stop changing production SQL without a new reproducible problem and plan evidence; retain the safe sequential probe for future regressions.

**Final WI-0202 operator review:** Check PostgreSQL health ready at schema v33, visit Archive (status and first photo page), open a formerly slow person-filtered Smart Collection, run an ad hoc person query, visit the complete Slideshow library and covers, then repeat Archive and Slideshows. Compare all visible results with the intended photo-only catalogue and exclusions. Confirm a usable verified backup/restore checkpoint for the **currently app-connected** database before closing. Previous ANALYZE recovery was operationally effective but its dominant planner cause remains unproven.

**Separate storage policy:** Approximately 13,802 historical provisional-cluster runs, 12,212 superseded, accompany a ~16 GB derived cluster-membership table. The huge B-tree indexes do not directly enter Smart Collection query SQL. A later retention design must preserve current run pointers, review constraints and evidence; the provisional_face_not_same_constraints.source_run_id foreign key uses ON DELETE SET NULL, so deleting runs can silently erase provenance. Use a dry-run policy and isolated restore rehearsal; do not prune in WI-0202.


## WI-0202 final disposition — 2026-10-19

**Completed for functional acceptance.** The maintainer reported "Everything seems to be working" following the final merged query optimization. The last sequential local saved-cover probe returned HTTP 200 for **all 28/28** requests, with people+relationship #7 at **1,029 / 1,078 ms** and the two previously slow named-place+orientation collections at **134–145 ms**. Earlier logged Archive, ad hoc Smart Collection and slideshow UI checks also passed.

The prior sections remain as historical diagnostic steps and baselines, not as a request for more index/SQL tuning. Work-item completion does not establish a definitive causal proof for the earlier scoped-ANALYZE recovery, independently verify a backup/restore of the currently connected database, or establish cold-start/concurrent performance beyond submitted evidence. In future operations confirm backup safety before maintenance, and investigate new observed regressions using the anonymous probe and SQL-phase warnings rather than applying routine unconditional ANALYZE, reindexing or derived-run pruning. Historical face-cluster retention must be scoped separately.
