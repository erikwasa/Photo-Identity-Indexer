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
- PR #282 adds a live PostgreSQL scale-acceptance test using an isolated disposable database with 96 targets and 32 confirmed exemplars. It verifies eight-target progress boundaries, concurrent status reads with a two-second cancellation bound, final durable counts, zero target/run failures, and the identity-regeneration throughput counters.
- `verify-postgres.ps1` selects `PostgresRuntimeApplicationTests`, so the scale acceptance runs during maintainer PostgreSQL verification without touching the production catalogue.

## Acceptance criteria
- [x] Invariant exemplar evidence is not reread from PostgreSQL for every target.
- [x] Target processing uses bounded batches and commits recoverable progress.
- [x] Restart resumes without duplicating completed target work.
- [x] UI polling/status reads remain responsive during active regeneration.
- [x] Correctness tests prove ranking/rejection/evidence-version semantics remain unchanged.

## Final maintainer acceptance (2026-09-11)

The maintainer ran `verify-postgres.ps1 -SkipContainerStart` against the existing PostgreSQL runtime after PR #282 was merged. The Release solution build completed with zero warnings and zero errors. The complete PostgreSQL persistence acceptance set passed 29/29 tests with zero skips, and the PostgreSQL runtime/composition acceptance set passed 7/7 tests with zero skips in 8 seconds.

That runtime/composition set includes the WI-0103 scale acceptance: 96 eligible targets, 32 confirmed exemplars, real PostgreSQL scoring in bounded eight-target cycles, a concurrent status read during every active cycle with a two-second cancellation bound, durable final counts, and regeneration throughput/failure counters. No status-read contention, target failures or run failures were observed.

WI-0103 is therefore complete. Exact scoring semantics remain unchanged; pgvector/approximate nearest-neighbor work remains optional follow-up rather than an M24 prerequisite.
