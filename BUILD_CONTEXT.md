# Build context

## Current milestone

**M02 — Local catalogue and jobs**

## Current work item

**WI-0011 — Add SQLite persistence**

Status: `in_progress`

## Branch and pull request

- Branch: `agent/WI-0011-face-persistence`
- Draft pull request: [#19 — Add transactional face inspection persistence](https://github.com/erikwasa/Photo-Identity-Indexer/pull/19)

## Objective

Establish versioned SQLite migrations and repositories for the local catalogue, human identity labels, model-derived observations and embeddings, and durable processing records.

## Current slice

Add the transactional persistence boundary for one complete face inspection result. The occurrence, detector observation, aligned crop and embedding are committed together, with natural-key resolution for safe reruns.

## Relevant files

- `src/PhotoIdentity.Persistence.Sqlite/FaceCatalogueRecords.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteFaceCatalogueRepository.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteAssetCatalogueRepository.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteCatalogueDatabase.cs`
- `tests/PhotoIdentity.Integration.Tests/SqliteFaceCatalogueRepositoryTests.cs`
- `docs/delivery/work-items/WI-0011-sqlite.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test for this slice

- Strong occurrence, crop and model identifiers round-trip without conversion leaking to callers.
- Occurrence, detector observation, crop and embedding rows are written in one transaction.
- A failed foreign-key write leaves no partial inspection rows.
- Reruns resolve an existing revision/ordinal occurrence and crop/protocol/hash to their stable identifiers.
- Detector output and crop storage metadata can be refreshed on a rerun.
- The first embedding remains immutable for an exact crop, model ID and model hash.
- Normalized geometry and embedding vectors round-trip without loss.

## Verification

The schema foundation merged in pull request #17 at `d1fa036ea256f8d5c9f8133ab184747908f0d64e`; GitHub Actions run `30173090694` passed.

The typed asset catalogue repository merged in pull request #18 at `2de0194b0835a8c9b8d13f08b6fa5311e855f889`; GitHub Actions run `30173892338` passed restore and vulnerability audit, Release build, all tests, living-document checks and Windows mixed-media verification.

Draft pull request #19 relies on GitHub Actions for executable validation because the agent environment does not contain the .NET SDK.

## Known issues

- This slice persists one detector observation, crop and embedding graph at a time; batch orchestration remains outside the repository.
- Identity records, processing records, backup behaviour and the concurrent writer policy remain before WI-0011 is complete.

## Next action

Resolve CI or review findings on pull request #19, then add typed people, human-label and identity-suggestion repositories.
