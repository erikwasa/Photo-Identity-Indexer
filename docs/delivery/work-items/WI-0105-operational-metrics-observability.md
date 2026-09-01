---
id: WI-0105
title: Add low-overhead PostgreSQL and background-work observability
milestone: M24
status_source: ../status/work-items.yaml
depends_on: [WI-0099]
related_adrs: [ADR-0009]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Persistence.Postgres, documentation]
---

# WI-0105: Add low-overhead PostgreSQL and background-work observability

## Objective
Make scaling regressions and stalled background work visible from the application without requiring repeated manual A/B performance runs.

## In scope
- Record coarse elapsed time and counts for synchronization, analysis, regeneration, gallery/status/settings queries and key background worker loops.
- Record queue/run counts, completed/failed/retried work and recent throughput using aggregation rather than per-item durable event spam.
- Surface database connectivity/pool pressure and slow-operation warnings where Npgsql/runtime metrics make this practical.
- Keep a bounded recent diagnostic view/log and document how to collect it.
- Avoid high-cardinality labels, personal filenames/paths, image data, hashes tied to personal content or embeddings.
- Establish lightweight regression tests for obviously unbounded/N+1 call patterns where feasible rather than machine-specific time thresholds.

## Acceptance criteria
- [ ] Operator can tell whether archive analysis/regeneration is progressing, stalled or repeatedly failing.
- [ ] Metrics overhead is intentionally bounded and does not add a database write per processed photo/face merely for telemetry.
- [ ] Slow API/background phases are identifiable without enabling verbose SQL logging.
- [ ] No sensitive photo-specific data is introduced into telemetry.
