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

- [ ] The solution builds with no SQLite persistence project.
- [ ] No active runtime/configuration path recognizes SQLite as a catalogue provider.
- [ ] Verification/packaging pass without SQLite assemblies.
- [ ] Current docs no longer describe SQLite as canonical/current.
- [ ] Historical records remain intact.

## Verification requirements

Full build/tests, package verification, PostgreSQL verification and docs validation/generation.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
