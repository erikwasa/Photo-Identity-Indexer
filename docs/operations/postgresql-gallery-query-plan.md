# PostgreSQL Face Gallery query-plan evidence

WI-0104 requires representative PostgreSQL evidence before adding catalogue-scale indexes to Face Gallery. Use the repository-root `measure-postgres-gallery-plan.ps1` probe while the local PostgreSQL container is running.

The probe is read-only. It runs `EXPLAIN (ANALYZE, BUFFERS, WAL, TIMING FALSE, FORMAT JSON)` for the current merged gallery query shapes against a selected PostgreSQL catalogue. `ANALYZE` executes the SELECT statements so the evidence includes actual rows and buffer activity; it does not modify catalogue rows.

## What the probe measures

The probe selects the most recently generated rank-one suggestion model revision inside the chosen catalogue and reads its persisted suggestion policy. It records catalogue scale plus four representative plans:

- **Needs review / Suggested person page** — the default suggestion-aware gallery ordering and the most important whole-catalogue sort path.
- **Needs review / Newest first page** — separates current-review filtering from suggestion-person ordering cost.
- **Needs review / All confidence count** — the optimized exact count path that should not join suggestion state.
- **Needs review / High confidence count** — the exact count path that legitimately requires rank-one suggestion state.

The page limit defaults to 40, matching the operator gallery.

## Run from the repository root

The PostgreSQL container must already be running. The probe discovers the running PostgreSQL container directly and does not read the launcher connection secret or `deploy/postgres/.env`.

```powershell
.\measure-postgres-gallery-plan.ps1
```

If exactly one database in that PostgreSQL server contains the Photo Identity catalogue marker tables, the probe selects it automatically. Rehearsal work commonly leaves multiple valid catalogue databases behind; in that case the probe refuses to guess and lists the candidates. Select the intended catalogue explicitly:

```powershell
.\measure-postgres-gallery-plan.ps1 `
  -DatabaseName <catalogue-database-name>
```

To keep the report at a known location:

```powershell
.\measure-postgres-gallery-plan.ps1 `
  -DatabaseName <catalogue-database-name> `
  -OutputPath "$env:TEMP\PhotoIdentity\gallery-plan.txt"
```

The probe intentionally does not start, stop, migrate or reconfigure PostgreSQL.

## Credential boundary

The probe executes `psql` through `podman exec` inside the already-running PostgreSQL container. Database/user/password values come from that container's existing `POSTGRES_*` environment and are not placed in the host command line or written to the report. The launcher connection secret and compose environment file are not read.

The generated report contains:

- capture timestamp and selected database name;
- schema version;
- aggregate face/rank-one-suggestion/active-review counts;
- model id/hash;
- the four JSON execution plans.

It intentionally omits connection strings and credentials. The report may contain PostgreSQL relation/index names and model identifiers, which are expected WI-0104 diagnostic evidence.

## Representative baseline (2026-09-11)

The maintainer-scale schema-23 rehearsal catalogue used for WI-0104 contained 18,281 face occurrences, 10,366 rank-one suggestions and 8,702 active review actions. With a 40-row page before PR #293:

- Needs review / Suggested person: about 806 ms and about 132k shared-buffer hits.
- Needs review / Newest first: about 255 ms and about 132k shared-buffer hits.
- Needs review / All confidence count: about 7.5 ms.
- Needs review / High confidence count: about 7.4 ms.

The page plans performed 18,281 latest-review-action index probes and then about 9,584 rank-one suggestion/suggestion/person lookups before applying the 40-row page limit. The count plan used a set-based anti join and estimated the 9,584 unreviewed faces correctly. This evidence supported changing the page current-state query shape before adding indexes.

## Representative after-plan (2026-09-11)

After PR #293 moved latest-review-action enrichment behind the page limit and changed page/navigation state membership to the set-based predicate, the same schema-23 catalogue measured:

- Needs review / Suggested person: 32.397 ms, 2,008 shared-buffer hits, no reads and no temporary I/O.
- Needs review / Newest first: 19.567 ms, 1,987 shared-buffer hits, no reads and no temporary I/O.
- Needs review / All confidence count: 3.973 ms.
- Needs review / High confidence count: 2.821 ms.

The page plans now build the 9,584 unreviewed-face set with a hash anti join, join rank-one suggestion state set-wise, use an in-memory top-N sort for the requested 40 rows, and only then resolve asset/crop/observation/latest-action details. The final review-action detail lookup performs 40 indexed searches rather than one probe for every catalogue face. At this scale the remaining sequential scans and top-N sort are inexpensive and selective-index changes are not justified by the representative evidence.

This after-plan closes WI-0104's Face Gallery page/scroll query acceptance slice. Retain the probe for future catalogue-growth regression checks rather than adding speculative indexes now.

## Interpreting the result

Review actual execution time, shared read/hit blocks, sort nodes, sequential scans and row counts before proposing indexes. A sequential scan alone is not evidence of a missing index: PostgreSQL may correctly choose one when most catalogue rows must be inspected. Add or alter an index only when the representative plan shows a selective lookup/sort that is materially expensive and the proposed index matches the production predicate/order shape.

If one scenario hits the configured statement timeout, retain the partial report. A representative timeout is itself acceptance evidence that the query shape still needs corrective work; do not raise the timeout merely to make the probe pass.

After any future query/index correction, rerun the same probe against the same representative catalogue and compare the before/after plans.
