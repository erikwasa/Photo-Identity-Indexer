# PostgreSQL Face Gallery query-plan evidence

WI-0104 requires representative PostgreSQL evidence before adding catalogue-scale indexes to Face Gallery. Use the repository-root `measure-postgres-gallery-plan.ps1` probe after the PostgreSQL-authoritative runtime is configured and the local PostgreSQL container is running.

The probe is read-only. It runs `EXPLAIN (ANALYZE, BUFFERS, WAL, TIMING FALSE, FORMAT JSON)` for the current merged gallery query shapes against the configured PostgreSQL catalogue. `ANALYZE` executes the SELECT statements so the evidence includes actual rows and buffer activity; it does not modify catalogue rows.

## What the probe measures

The probe automatically selects the most recently generated rank-one suggestion model revision and reads its persisted suggestion policy. It records catalogue scale plus four representative plans:

- **Needs review / Suggested person page** — the default suggestion-aware gallery ordering and the most important whole-catalogue sort path.
- **Needs review / Newest first page** — separates current-review filtering from suggestion-person ordering cost.
- **Needs review / All confidence count** — the optimized exact count path that should not join suggestion state.
- **Needs review / High confidence count** — the exact count path that legitimately requires rank-one suggestion state.

The page limit defaults to 40, matching the operator gallery.

## Run from the repository root

The production launcher stores the connection string under the named Windows environment variable. The probe checks Process scope first and User scope second, so the connection string does not need to be copied into the command line.

```powershell
.\measure-postgres-gallery-plan.ps1
```

To keep the report at a known location:

```powershell
.\measure-postgres-gallery-plan.ps1 `
  -OutputPath "$env:TEMP\PhotoIdentity\gallery-plan.txt"
```

The PostgreSQL container must already be running. The probe intentionally does not start, stop, migrate, or reconfigure PostgreSQL.

## Credential boundary

The probe never prints or writes the PostgreSQL connection string or password. It reads the database/user identity from the already-configured runtime connection string, verifies that the runtime user matches `deploy/postgres/.env`, and then executes `psql` inside the existing PostgreSQL container using the container's own `POSTGRES_PASSWORD` environment variable. The password is therefore not placed in the host command line.

The generated report contains:

- capture timestamp;
- schema version;
- aggregate face/rank-one-suggestion/active-review counts;
- model id/hash;
- the four JSON execution plans.

It intentionally omits the connection string and credentials. The report may contain PostgreSQL relation/index names and model identifiers, which are expected WI-0104 diagnostic evidence.

## Interpreting the result

Review actual execution time, shared read/hit blocks, sort nodes, sequential scans and row counts before proposing indexes. A sequential scan alone is not evidence of a missing index: PostgreSQL may correctly choose one when most catalogue rows must be inspected. Add or alter an index only when the representative plan shows a selective lookup/sort that is materially expensive and the proposed index matches the production predicate/order shape.

If one scenario hits the configured statement timeout, retain the partial report. A representative timeout is itself acceptance evidence that the query shape still needs corrective work; do not raise the timeout merely to make the probe pass.

After any query/index correction, rerun the same probe against the same representative catalogue and compare the before/after plans before marking the Face Gallery page/scroll acceptance criterion complete.
