---
id: WI-0147
title: Make PostgreSQL the unconditional runtime catalogue
milestone: M29
status_source: ../status/work-items.yaml
depends_on: [WI-0102, WI-0106]
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Cli, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Integration.Tests, docs]
---

# WI-0147: Make PostgreSQL the unconditional runtime catalogue

## Objective

Remove the runtime catalogue-provider switch and bind normal application/CLI composition directly to PostgreSQL.

## Why

M24 established PostgreSQL as the sole writable production catalogue, but composition still carries a complete selectable SQLite path and defaults to SQLite when configuration is absent.

## In scope

- Remove normal runtime selection of SQLite from API/CLI composition.
- Make PostgreSQL configuration/health requirements explicit.
- Remove provider-conditional runtime branches that only serve dual support.
- Update tests and operator guidance.
- Retain historical migration evidence until later cleanup decides what can be deleted.

## Out of scope

- Deleting historical delivery records.
- Unrelated PostgreSQL schema changes.

## Acceptance criteria

- [ ] Normal startup has no supported SQLite provider mode.
- [ ] Missing PostgreSQL config fails clearly rather than selecting SQLite.
- [ ] Provider-conditional runtime paths are removed or justified as compatibility tools.
- [ ] Runtime/integration tests protect PostgreSQL expectations.
- [ ] Operator docs describe PostgreSQL as the active catalogue.

## Verification requirements

Build, relevant tests and packaged/runtime startup verification.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
