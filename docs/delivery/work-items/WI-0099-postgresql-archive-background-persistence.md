---
id: WI-0099
title: Migrate archive and background-processing persistence to PostgreSQL
milestone: M24
status_source: ../status/work-items.yaml
depends_on: [WI-0098]
related_adrs: [ADR-0009]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Worker, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Persistence.Sqlite]
---

# WI-0099: Migrate archive and background-processing persistence to PostgreSQL

## Objective
Move the high-concurrency archive/background writer domains to PostgreSQL and remove the host-shutdown failure mode seen under SQLite contention.

## In scope
- Implement PostgreSQL persistence for archive coverage, observations, availability, verification, analysis/post-analysis, hydration, storage and advancement control.
- Migrate automatic Places enrichment operational state needed by its hosted worker.
- Preserve resumability, idempotency and OneDrive hydration/release ownership semantics.
- Add top-level hosted-service resilience so transient database failures are logged/retried and failure-reporting writes cannot escape and terminate the host.
- Ensure one background worker failure does not silently stop all future work.
- Add concurrency-focused integration tests with archive advancement, enrichment and another writer active together.

## Acceptance criteria
- [ ] Archive/background state can run entirely against PostgreSQL.
- [ ] Concurrent background writes do not produce the prior SQLite table-lock shutdown class.
- [ ] A transient database exception cannot terminate Photo Identity through an escaping recovery write.
- [ ] Durable run/lease/retry state survives application restart.
- [ ] No personal paths/content are emitted by new diagnostics beyond existing privacy-safe conventions.
