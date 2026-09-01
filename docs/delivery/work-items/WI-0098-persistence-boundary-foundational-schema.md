---
id: WI-0098
title: Add database-neutral persistence boundary and foundational PostgreSQL schema
milestone: M24
status_source: ../status/work-items.yaml
depends_on: [WI-0097]
related_adrs: [ADR-0009]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Api, tests]
---

# WI-0098: Add database-neutral persistence boundary and foundational PostgreSQL schema

## Objective
Stop application/domain code from depending directly on SQLite implementation types and implement the foundational catalogue/job model in PostgreSQL.

## In scope
- Introduce interfaces/contracts at the application boundary for foundational catalogue and processing persistence.
- Remove direct construction of SQLite repositories from affected application services.
- Create PostgreSQL schema/migrations for sources, assets, revisions, face occurrences/observations/crops, embeddings, processing runs/jobs and required reference data.
- Preserve stable identifiers, uniqueness rules, foreign-key semantics and immutable-revision behavior.
- Keep SQLite adapters behind the same contracts while migration is incomplete.
- Add contract/integration tests that run the same behavioral expectations against both adapters where practical.

## Acceptance criteria
- [ ] Foundational application paths depend on persistence contracts rather than `Sqlite*` concrete types.
- [ ] PostgreSQL schema represents existing foundational entities without lossy type conversions.
- [ ] Processing lease/idempotency/restart semantics remain intact.
- [ ] Existing SQLite behavior stays green during the transition.
- [ ] New authoritative persistence work follows the PostgreSQL-capable boundary.
