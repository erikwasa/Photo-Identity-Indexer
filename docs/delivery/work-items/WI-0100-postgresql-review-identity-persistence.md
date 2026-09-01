---
id: WI-0100
title: Migrate review and identity persistence to PostgreSQL
milestone: M24
status_source: ../status/work-items.yaml
depends_on: [WI-0098]
related_adrs: [ADR-0009]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Persistence.Sqlite]
---

# WI-0100: Migrate review and identity persistence to PostgreSQL

## Objective
Implement PostgreSQL-backed review, people, suggestion, policy and identity-match persistence with unchanged operator semantics.

## In scope
- Review actions including assign/unknown/reject/undo and suggestion accept/reject history.
- People, labels, favorites/featured visibility and maintenance history.
- Identity suggestions, rankings, policy/evidence-version state and regeneration run/target state.
- Bulk review/suggestion operations and audit queries.
- Preserve canonical review history, merge semantics and stale-evidence guarantees.
- Add PostgreSQL integration coverage for representative single, bulk and restart/recovery workflows.

## Acceptance criteria
- [ ] Face review and identity workflows execute against PostgreSQL without SQLite authoritative writes.
- [ ] Existing IDs/history and accepted/rejected suggestion semantics map losslessly.
- [ ] Regeneration run state remains durable/resumable; algorithmic scaling is deferred to WI-0103.
- [ ] Review/audit behavior matches current accepted semantics.
