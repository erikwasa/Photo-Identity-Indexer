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

Started 2026-09-03. Active review: PR #259.

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


### Maintainer verification — PostgreSQL schema version 12

PR #259 merged on 2026-09-03 at `679f69929e8b556b0700ab294b5391431b1a3141`; workflow #1494 passed and maintainer `verify-postgres.ps1` verification succeeded.

Schema version 12 and ranked suggestion read/accept/reject persistence are accepted.

## Slice 3 — bulk face and grouped-suggestion review

Started 2026-09-03. Active review: PR #260.

- Added provider-neutral bulk face-review and grouped-suggestion preview/commit contracts plus Core-owned preview/result DTOs.
- `SqliteBulkReviewRepository` and `SqliteBulkSuggestionReviewRepository` implement the neutral contracts through compatibility mappings; existing public SQLite APIs remain intact.
- Added `PostgresBulkReviewRepository` and `PostgresBulkSuggestionReviewRepository`. No schema migration is required; the slice uses schema version 12.
- PostgreSQL preview tokens use the same deterministic payload as SQLite: action/model/person + requested IDs + currently eligible IDs.
- Bulk commits lock selected face rows, re-read eligibility and recompute the preview token before any write. Stale previews fail closed rather than partially applying.
- Grouped suggestion commit additionally locks the selected suggestion rows, requires exact rank-one matches for one active person/model revision, and accepts only currently pending suggestions whose faces remain unreviewed.
- General bulk assignment writes the same manual labels and canonical review actions as single-face review; Unknown/reject remain canonical personless review actions.
- Grouped suggestion acceptance writes the same manual label, canonical assignment and linked suggestion accept history as single-suggestion acceptance.
- The bulk HTTP endpoints now depend on neutral contracts, while DI still resolves them to SQLite. Runtime review authority is unchanged.
- Live PostgreSQL verification covers successful two-face assignment, stale-preview conflict after an intervening review, grouped rank-one acceptance, skipped already-reviewed suggestion behavior, canonical assignment history and durable suggestion statuses.

Next WI-0100 persistence layers are person maintenance/audit, suggestion gallery/policy/evidence state and identity regeneration run/target state.


### Maintainer verification — bulk review on schema version 12

PR #260 merged on 2026-09-03 at `07dddf3d0120d41859cf5a5816c251135d21159a`; workflow #1497 passed and maintainer `verify-postgres.ps1` verification succeeded.

Bulk face review and grouped rank-one suggestion acceptance are accepted against schema version 12.

## Slice 4 — canonical person maintenance and merge audit

Started 2026-09-03. Active review: PR #261.

- Added provider-neutral `IPersonMaintenanceRepository` plus Core-owned person-maintenance person/action records.
- `SqlitePersonMaintenanceRepository` implements the neutral contract through compatibility mappings while retaining its existing public API.
- Added PostgreSQL schema version 13 with `person_favorites` and append-only `person_maintenance_actions`. The favorites table is a narrow prerequisite because person merge must preserve favorite status.
- Added `PostgresPersonMaintenanceRepository` for active-person listing, maintenance history, audited rename and explicitly confirmed irreversible merge.
- PostgreSQL rename row-locks the active person, changes the display name and appends a reversible maintenance action in one transaction.
- PostgreSQL merge deterministically row-locks source/target people, consolidates duplicate/manual labels and canonical review-action references, consolidates suggestions/review history/ranking references using the same accepted > rejected > pending status precedence as SQLite, carries favorite state to the target, marks the source merged and appends the irreversible audit record in one transaction.
- The maintenance API rename/merge/list/history repository dependency is now provider-neutral. Adjacent favorite/featured-face/smart-collection-visibility helper endpoints remain SQLite-specific and are outside this authoritative maintenance slice.
- Runtime DI still resolves the neutral maintenance contract to SQLite; no provider switch is introduced.
- Live PostgreSQL verification covers rename history, explicit irreversible-merge confirmation, duplicate label remapping, review-action person/label transfer, duplicate suggestion consolidation, ranking/history transfer, favorite carryover, merged-person exclusion from active listing and maintenance-history durability after repository recreation.

The richer read-only person audit view, suggestion gallery/policy/evidence state and identity regeneration run/target state remain later WI-0100 slices.

## Slice 5 — read-only person audit view

Started 2026-09-03. Reviewed in PR #262.

- Added provider-neutral `IPersonAuditRepository`, a SQLite compatibility adapter and `PostgresPersonAuditRepository` over schema version 13.
- PostgreSQL audit queries preserve active-assignment history, exact-model top-suggestion comparison, disagreement counting/filtering, accepted sort semantics, crop/observation metadata, pagination and merged-person exclusion.
- No schema migration or runtime provider switch was required.
- Focused live PostgreSQL coverage verifies agreement/disagreement semantics and validation.

### Maintainer verification — person audit on schema version 13

PR #262 merged on 2026-09-03 at `c22131c868667138d100a90be6a3517ee33b2321`; maintainer `verify-postgres.ps1` verification succeeded.

The PostgreSQL person-audit view is accepted.

## Slice 6 — identity evidence-version reader

Started 2026-09-03. Reviewed in PR #263.

- Added provider-neutral `IIdentityMatchEvidenceVersionReader` and Core-owned evidence-version state.
- Added a SQLite compatibility adapter over the existing accepted reader and `PostgresIdentityMatchEvidenceVersionReader` over existing schema version 13 tables.
- PostgreSQL preserves catalogue-wide review-action, suggestion-decision and person-merge counters while scoping embedding evidence to the exact model revision.
- The automatic-assignment expected-counter adjustment is provider-neutral.
- Runtime regeneration composition remains SQLite-backed.
- Focused live PostgreSQL coverage verifies empty state, all four counters, exact-model embedding isolation and expected automatic-assignment adjustment.

### Maintainer verification — identity evidence on schema version 13

PR #263 merged on 2026-09-03 at `fb7d47bafcc13df87d57eff5cc9ea6bdbb50b1db`; maintainer `verify-postgres.ps1` verification succeeded.

The PostgreSQL identity evidence-version reader is accepted.

## Slice 7 — exact-model suggestion policy persistence

Started 2026-09-03.

- Added Core-owned `ReviewIdentitySuggestionPolicy` and provider-neutral `IIdentitySuggestionPolicyRepository` with the accepted confidence classification and validation semantics.
- Added `SqliteIdentitySuggestionPolicyAdapter`; the suggestion-policy HTTP endpoint now depends on the neutral contract while runtime DI still resolves it to SQLite.
- Added PostgreSQL schema version 14 with exact-model `identity_suggestion_policies`, monotonic policy versioning fields and database-level score/margin/model validation.
- Added `PostgresIdentitySuggestionPolicyRepository` preserving default initialization, no-op updates, exact-model isolation and durable versioned changes. Updates lock the exact policy row before incrementing the version.
- Focused live PostgreSQL coverage verifies defaults, exact-model isolation, changed/no-op updates, restart durability and invalid-policy rejection.

Suggestion gallery persistence and identity regeneration run/target durability remain later WI-0100 slices. Runtime review/identity authority remains SQLite until controlled cutover.
