---
id: WI-0103
title: Make identity match regeneration scalable and bounded
milestone: M24
status_source: ../status/work-items.yaml
depends_on: [WI-0100]
related_adrs: [ADR-0009]
affected_modules: [PhotoIdentity.Persistence.Postgres, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0103: Make identity match regeneration scalable and bounded

## Objective
Remove the current per-target reload of invariant evidence and make regeneration progress predictably on a large PostgreSQL catalogue.

## In scope
- Snapshot/load confirmed exemplar evidence once per regeneration run or bounded evidence epoch rather than once per target.
- Avoid full rejected-pair reload for every target; use an efficient target-scoped or precomputed structure.
- Claim/process/persist targets in bounded batches while retaining durable restart/resume and evidence-version stale semantics.
- Bulk-create regeneration targets rather than issuing one insert per target.
- Keep memory bounded; do not load the entire growing target corpus unnecessarily.
- Add progress/throughput/failure metrics with negligible per-target overhead.
- Preserve exact current scoring semantics initially; pgvector/ANN is explicitly optional follow-up work.

## Implementation progress

- PR #280 prepares invariant PostgreSQL exemplar evidence once per durable run, reads rejected people per target instead of loading the full rejected-pair corpus, and advances the hosted worker in bounded eight-target cycles while retaining per-target commits and restart recovery.
- PR #281 moves target snapshot creation fully into PostgreSQL with one `INSERT ... SELECT`, so run creation no longer materializes all target IDs in application memory or performs one insert round trip per target.
- The live PostgreSQL scale-acceptance test uses an isolated disposable database with 96 targets and 32 confirmed exemplars. It verifies eight-target progress boundaries, concurrent status reads with a two-second cancellation bound, final durable counts, zero target/run failures, and the identity-regeneration throughput counters.
- `verify-postgres.ps1` already selects `PostgresRuntimeApplicationTests`, so the scale acceptance runs automatically during the maintainer PostgreSQL verification without touching the production catalogue.

## Acceptance criteria
- [x] Invariant exemplar evidence is not reread from PostgreSQL for every target.
- [x] Target processing uses bounded batches and commits recoverable progress.
- [x] Restart resumes without duplicating completed target work.
- [ ] UI polling/status reads remain responsive during active regeneration.
- [x] Correctness tests prove ranking/rejection/evidence-version semantics remain unchanged.

The remaining unchecked criterion requires the live PostgreSQL scale acceptance to pass in the maintainer environment. If it exposes status-read contention or throughput regressions, corrective query/index work remains part of WI-0103 before closeout.
