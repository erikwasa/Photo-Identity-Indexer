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

## Acceptance criteria
- [ ] Invariant exemplar evidence is not reread from PostgreSQL for every target.
- [ ] Target processing uses bounded batches and commits recoverable progress.
- [ ] Restart resumes without duplicating completed target work.
- [ ] UI polling/status reads remain responsive during active regeneration.
- [ ] Correctness tests prove ranking/rejection/evidence-version semantics remain unchanged.
