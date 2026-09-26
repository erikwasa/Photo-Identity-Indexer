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

- [x] Normal startup has no supported SQLite provider mode.
- [x] Missing PostgreSQL config fails clearly rather than selecting SQLite.
- [x] Provider-conditional runtime paths are removed or justified as compatibility tools.
- [x] Runtime/integration tests protect PostgreSQL expectations.
- [x] Operator docs describe PostgreSQL as the active catalogue.

## Verification requirements

Build, relevant tests and packaged/runtime startup verification.

## Completion notes

- Files changed: API catalogue composition/startup and PostgreSQL-only endpoint branches; detector-rollout CLI parsing/composition; Windows launcher, backup helper, examples and verifier; runtime/integration tests; active architecture/operator documentation.
- Trade-offs: existing API integration tests retain an isolated `IntegrationTest` SQLite compatibility graph so WI-0147 can remove the operator/runtime provider switch without forcing the separate WI-0148 test migration into this item. The normal launcher cannot select this environment.
- Deferred work: WI-0148 ports or retires the remaining SQLite-dependent tests/tools; WI-0149 removes the SQLite project, the compatibility composition and its API project reference.
- Test layer and CI impact: composition and CLI assertions remain at the integration-test layer because they cover executable service registration, host startup and command parsing. No required CI gate changed. The complete 652-test integration assembly passed in 5m09s Debug and 4m13s Release; the runtime-composition-focused set passed 13/13 in 1 second.
- Commands run: `dotnet restore PhotoIdentity.slnx`; `./build.ps1`; focused `dotnet test` filters for catalogue composition, PostgreSQL runtime, rollout CLI and host regressions; complete Debug integration assembly (652/652); `./test.ps1` (all suites passed); launcher `-ValidateConfigurationOnly`; direct API startup without PostgreSQL configuration (expected explicit failure); `PhotoIdentity.Docs validate`; `PhotoIdentity.Docs generate --check`. `./verify-postgres.ps1` was attempted but this isolated worktree has no private `deploy/postgres/.env`; no live PostgreSQL claim is made by this item.
