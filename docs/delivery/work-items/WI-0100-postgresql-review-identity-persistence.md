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


## Slice 1 — canonical people and review actions

Started 2026-09-03. Active review: PR #258.

- Added Core-owned `ReviewPerson`, `ReviewAction`, `ReviewActionKinds` and provider-neutral `IReviewActionRepository`.
- `SqliteReviewRepository` implements the neutral contract through explicit compatibility mappings; its existing SQLite public API remains intact.
- Added PostgreSQL schema version 11 with `people`, `person_labels`, `review_actions`, base `identity_suggestions` and `identity_suggestion_review_actions`.
- The suggestion/junction tables are included here only because canonical undo must restore an accepted suggestion to `pending`; ranking, policy, gallery and regeneration persistence remain later WI-0100 slices.
- Added `PostgresReviewActionRepository` for person creation, manual assignment, Unknown, rejection, undo and append-only history reads.
- PostgreSQL undo row-locks the latest active review action, reverses it atomically, restores linked accepted suggestions to `pending`, and appends the explicit undo history row.
- Runtime dependency injection exposes the neutral review-action contract but still resolves it to `SqliteReviewRepository`; endpoints have not switched providers.
- Extended live PostgreSQL verification to cover person creation, assignment/manual label persistence, accepted-suggestion restoration on undo, Unknown/reject/undo ordering, reversal timestamps and history durability after repository recreation.

This slice does not perform runtime cutover and does not claim bulk review, suggestion workflows, person maintenance or identity regeneration acceptance.


### Maintainer verification — PostgreSQL schema version 11

PR #258 merged on 2026-09-03 at `384f74057b3fb80ec5895012f96787604c450ee7`; workflow #1489 passed and maintainer `verify-postgres.ps1` verification succeeded.

Schema version 11 and canonical person/manual-label/review-action persistence are accepted, including accepted-suggestion restoration on undo.

## Slice 2 — ranked suggestion review decisions

Started 2026-09-03.

- Added provider-neutral ranked suggestion and suggestion-decision records plus `IReviewSuggestionRepository`.
- `SqliteReviewSuggestionRepository` implements the neutral contract through compatibility mappings while retaining its existing public API.
- Added PostgreSQL schema version 12 with `identity_suggestion_rankings`; v11 already owns the base suggestions and suggestion-review history tables.
- Added `PostgresReviewSuggestionRepository` for ranked suggestion reads and explicit accept/reject decisions.
- PostgreSQL accept/reject locks the face occurrence row before decision processing, preserving one active human decision per face under concurrent reviewers.
- Accept atomically upserts the manual person label, appends the canonical assignment action, changes the suggestion to accepted and records its linked accept decision.
- Reject changes only the suggestion to rejected and records suggestion-decision history; it does not reject the face itself.
- Runtime DI exposes the neutral suggestion contract but still resolves it to SQLite. Endpoint authority is unchanged.
- Live PostgreSQL verification covers pending reads, accept → canonical assignment, repeated-decision rejection, suggestion-only rejection and restart persistence.

Bulk suggestion review, suggestion gallery/policy, person maintenance and identity regeneration remain later WI-0100 slices.
