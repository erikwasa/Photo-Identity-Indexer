---
id: WI-0148
title: Retire SQLite-dependent tests and compatibility tools that no longer protect supported behavior
milestone: M29
status_source: ../status/work-items.yaml
depends_on: [WI-0147]
related_adrs: []
affected_modules: [PhotoIdentity.Persistence.Tests, PhotoIdentity.Integration.Tests, PhotoIdentity.Bundle.Tests, tools, docs]
---

# WI-0148: Retire SQLite-dependent tests and compatibility tools that no longer protect supported behavior

## Objective

Remove SQLite dependencies from active test/tooling paths once runtime dual-provider support is gone.

## Why

Test projects and migration-era utilities can otherwise keep a second persistence implementation compiled and maintained indefinitely.

## In scope

- Inventory active references to PhotoIdentity.Persistence.Sqlite.
- Port tests protecting current behavior to PostgreSQL/provider-neutral contracts.
- Retire tools used only for normal SQLite operation.
- Keep migration evidence only for an explicit ongoing recovery need.
- Remove dead dual-provider fixtures and measure CI impact.

## Out of scope

- Deleting historical documents that mention SQLite.
- Dropping useful coverage solely to reduce files.

## Acceptance criteria

- [ ] Supported behavior no longer depends on SQLite adapters except explicitly documented temporary migration exceptions.
- [ ] Removed tests have equivalent current coverage or documented obsolescence.
- [ ] Active tooling no longer offers SQLite as a normal catalogue.
- [ ] Remaining SQLite references are enumerated for WI-0149.

## Verification requirements

Relevant suites and documentation validation/generation.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
