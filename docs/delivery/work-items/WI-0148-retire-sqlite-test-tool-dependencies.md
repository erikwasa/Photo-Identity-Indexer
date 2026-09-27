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

- Files changed: added `PhotoIdentity.TestSupport.Postgres`; moved persistence and API integration fixtures to isolated PostgreSQL databases; removed SQLite references from persistence, integration, bundle and review-verification test/tool projects; provisioned PostgreSQL in CI integration shards; updated affected test fixtures, PostgreSQL behavior and active testing/runtime documentation.
- Trade-offs: mature integration fixtures temporarily retain `Sqlite*` source aliases backed exclusively by Npgsql/PostgreSQL test support. This keeps the port reviewable while preventing a compiled SQLite test dependency; WI-0149 removes the compatibility names together with the adapter.
- Removed or obsolete coverage: provider-schema migration tests, SQLite backup/match tests and normal-operation CLI tests for archive, batch, bundle and catalogue evaluation were removed from the active integration assembly. The SQLite-source catalogue-migration rehearsal is preserved as accepted WI-0102 operational evidence rather than keeping the provider compiled into the active integration suite. Current PostgreSQL behavior remains covered at repository/application layers; the removed CLI paths are retained migration/compatibility surface awaiting WI-0149 disposition, not supported PostgreSQL runtime behavior.
- WI-0149 inventory: remove `PhotoIdentity.Persistence.Sqlite` from `PhotoIdentity.slnx` and `Microsoft.Data.Sqlite` from package management; remove the remaining direct SQLite references and compatibility composition in `PhotoIdentity.Api` and `PhotoIdentity.Cli`; retire or replace SQLite migration/backup, archive, batch, bundle, evaluation-export and match commands; delete excluded legacy test sources and the PostgreSQL test aliases whose names still say `Sqlite`; update remaining active SQLite-era architecture/operator documents. The ReviewVerification tool, bundle tests, persistence tests and compiled integration tests no longer reference the SQLite project.
- CI impact: the required integration surface is 577 tests across two balanced shards. A sequential local PostgreSQL run completed shard 1 (273 tests) in 3m35s and shard 2 (304 tests) in 3m56s; CI retains two-way sharding and provisions PostgreSQL explicitly.
- Commands run: `./build.ps1` (Release, zero warnings/errors); `./test.ps1` (established the new PostgreSQL prerequisite); both CI integration shards against local PostgreSQL (577/577 passed); targeted regression runs; `PhotoIdentity.Docs validate`; `PhotoIdentity.Docs generate --check`.
