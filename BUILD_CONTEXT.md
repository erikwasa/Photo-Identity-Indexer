# Build context

## Current milestone

**M02 — Local catalogue and jobs**

## Current work item

**WI-0011 — Add SQLite persistence**

Status: `in_progress`

## Branch and pull request

- Branch: `agent/WI-0011-sqlite-foundation`
- Draft pull request: [#17 — Add SQLite catalogue foundation](https://github.com/erikwasa/Photo-Identity-Indexer/pull/17)

## Objective

Establish versioned SQLite migrations and repositories for the local catalogue, human identity labels, model-derived observations and embeddings, and durable processing records.

## Current slice

The first M02 slice creates schema version 1 and validates the storage invariants before repository CRUD is added.

## Relevant files

- `src/PhotoIdentity.Persistence.Sqlite/SqliteCatalogueDatabase.cs`
- `src/PhotoIdentity.Persistence.Sqlite/PhotoIdentity.Persistence.Sqlite.csproj`
- `tests/PhotoIdentity.Integration.Tests/SqliteCatalogueDatabaseTests.cs`
- `docs/delivery/work-items/WI-0011-sqlite.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test for this slice

- A new temporary database migrates to schema version 1.
- Repeated initialisation is idempotent.
- Foreign keys are enabled on opened connections.
- Human labels can be written without observations, crops, embeddings or suggestions.
- Multiple embedding model hashes can coexist for the same crop and model ID.
- Duplicate crop/model/model-hash embeddings are rejected.

## Verification

M01 is complete. Pull request #16 merged at `4afa704dd6032cefcae598f544d3166c1107692e`, GitHub Actions run `30169777119` passed, and the developer verified the complete M01 workflow on representative private images.

The agent environment does not contain the .NET SDK, so draft pull request #17 relies on GitHub Actions for executable validation.

## Known issues

- Schema version 1 is intentionally repository-neutral; typed repository implementations are not part of this first slice.
- Backup, concurrent writer behaviour and future schema upgrades remain to be specified before WI-0011 is completed.

## Next action

Resolve CI or review findings on pull request #17, then add typed repositories and transactional round-trip tests over the approved schema.
