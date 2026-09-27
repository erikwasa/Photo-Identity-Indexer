---
id: WI-0149
title: Remove the SQLite implementation and obsolete migration-era active references
milestone: M29
status_source: ../status/work-items.yaml
depends_on: [WI-0148]
related_adrs: []
affected_modules: [PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Cli, tests, tools, docs]
---

# WI-0149: Remove the SQLite implementation and obsolete migration-era active references

## Objective

Delete the SQLite persistence project and remaining active compatibility/migration code once no supported path requires it.

## Why

Keeping a second full persistence implementation after PostgreSQL cutover increases maintenance, compile surface and architectural ambiguity.

## In scope

- Remove PhotoIdentity.Persistence.Sqlite project references and solution membership.
- Delete obsolete SQLite provider composition and migration/rehearsal utilities no longer required for supported recovery.
- Update active architecture/product/operator documentation to PostgreSQL terminology.
- Preserve historical milestone/work-item/ADR records.
- Confirm active examples/scripts no longer select SQLite.

## Out of scope

- Rewriting historical documents to erase SQLite history.
- Deleting private backups/databases.

## Acceptance criteria

- [x] The solution builds with no SQLite persistence project.
- [x] No active runtime/configuration path recognizes SQLite as a catalogue provider.
- [x] Verification/packaging pass without SQLite assemblies.
- [x] Current docs no longer describe SQLite as canonical/current.
- [x] Historical records remain intact.

## Verification requirements

Full build/tests, package verification, PostgreSQL verification and docs validation/generation.

## Completion notes

- Files changed: removed `PhotoIdentity.Persistence.Sqlite`, provider-selection composition, obsolete catalogue migration/backup/archive/batch/match/evaluation/bundle commands, migration rehearsal scripts and their retired tests; normalized remaining PostgreSQL integration fixtures and aliases; updated current architecture, product, package and operator documentation while retaining explicitly historical migration records.
- Trade-offs: mature integration fixtures retain PostgreSQL-only compatibility helpers for legacy fixture shapes and SQL parameter normalization, but no active project references `Microsoft.Data.Sqlite`, exposes a SQLite catalogue option or uses a SQLite-prefixed test identifier. Historical cutover/runbook content remains clearly labelled as non-executable evidence.
- Deferred work: none for M29. Any future remote-compute or alternate catalogue path requires a new ADR and newly scoped work.
- Commands run: `./build.ps1`; `./test.ps1` against an isolated PostgreSQL service (839 tests passed, including 574 host-heavy integration tests); `./verify-postgres.ps1 -SkipContainerStart` (53 persistence and 6 runtime/composition acceptance tests passed); `./verify-package.ps1 -Configuration Release`; `PhotoIdentity.Docs validate`; `PhotoIdentity.Docs generate --check`.
