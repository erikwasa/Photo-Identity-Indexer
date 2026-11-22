---
id: WI-0087
title: Add authoritative exact-duplicate source-copy inventory
milestone: M23
status_source: ../status/work-items.yaml
depends_on: [WI-0041]
related_adrs: [ADR-0008]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Api, documentation]
---

# WI-0087: Add authoritative exact-duplicate source-copy inventory

## Objective

Detect and query byte-identical source copies across the permanent catalogue without merging their asset identity, revision history or operator lifecycle state.

## Why

The same photo can legitimately exist at multiple OneDrive source paths. Exact duplicate awareness is needed for operator review and for conservative move/rename reconciliation, but duplicate content does not imply that the source copies should be treated as one asset.

## In scope

- Add an efficient non-unique lookup/index over authoritative revision SHA-256 values.
- Define a query/service contract that returns exact duplicate groups as independently addressable source copies.
- Treat a duplicate group as two or more independently catalogued source copies with the same authoritative content hash.
- Use already verified immutable revision hashes when available, including a known revision whose current source availability is online-only.
- Do not claim exact duplication for a source item that has never been authoritatively hashed/verified.
- Keep source presence, duplicate membership and later exclusion state independent.
- Provide focused persistence/integration tests for multiple paths with the same bytes and for changed content at one path.

## Out of scope

- Merging AssetIds, revisions, face history, tags or review actions.
- Automatically choosing one duplicate as the only library-visible copy.
- Propagating exclusion between duplicate copies.
- Perceptual/near-duplicate detection for crops, resized, recompressed or edited images.
- Guessing equality from filename, file size or timestamps.

## Acceptance criteria

- [x] Two independently catalogued source paths with the same authoritative SHA-256 are returned in one exact-duplicate group.
- [x] Each duplicate entry retains its own source/asset identity.
- [x] A third non-matching source copy does not enter the group.
- [x] Changing one path's bytes creates/uses the appropriate new revision and removes that current revision from the old exact group.
- [x] Duplicate lookup is backed by an appropriate non-unique index and remains practical on the permanent catalogue.
- [x] No UNIQUE constraint is introduced across content SHA-256 values.
- [x] Online-only/current-source state does not erase a previously established immutable hash, but unverified metadata alone never establishes a duplicate.
- [x] Tests prove duplicate inventory does not merge or rewrite existing review/identity data.

## Verification requirements

Automated SQLite/integration coverage is required. Maintainer review should verify a small real-catalogue exact-duplicate example without committing filenames, hashes tied to personal files or image content.

## Completion notes

Implementation was merged to `main` in PR #426 on 2026-09-25 from
`agent/wi-0087-exact-duplicate-inventory`. Verification closeout resumed on
`codex/wi-0087-verification-closeout` on 2026-09-26.

- Files changed: added the provider-neutral exact-duplicate contract, SQLite and PostgreSQL query adapters, a read-only `/api/archive/exact-duplicates` endpoint, and focused provider tests.
- Semantics: duplicate membership uses the revision explicitly verified as current by the archive source observation. `needs-source-verification` and never-verified source copies are excluded; verified online-only and later-missing copies retain duplicate membership.
- Indexing: both adapters idempotently ensure a non-unique `(content_sha256, asset_id)` lookup index before querying. This keeps the slice deployable on both current PostgreSQL catalogues and legacy SQLite catalogues without introducing cross-copy uniqueness.
- Trade-offs: the operator UI remains intentionally deferred to WI-0091. The endpoint exposes source-copy identities and lifecycle presence but does not merge, suppress or mutate them.
- Verification hardening: the focused SQLite integration test now seeds an assigned face and proves that exact-duplicate reads preserve the face occurrence, person, confirmed label, append-only review action and effective assignment.
- Automated evidence on 2026-09-26: the focused SQLite test passed; `./build.ps1` passed with zero warnings and errors; `./test.ps1` passed 888 tests; `PhotoIdentity.Docs validate` and `generate --check` passed; and `./verify-postgres.ps1 -SkipContainerStart` passed 52 live PostgreSQL persistence tests plus 8 PostgreSQL runtime/composition integration tests.
- Maintainer real-catalogue evidence on 2026-09-26: `/api/archive/exact-duplicates` completed normally against the permanent PostgreSQL catalogue and returned one group containing 1,878 independently catalogued copies. The maintainer inspected members, confirmed they were genuine copies, and confirmed copies retained different AssetIds. No private filenames, source paths or hashes are recorded in delivery evidence.
- Status: completed on 2026-09-26 after maintainer acceptance.
