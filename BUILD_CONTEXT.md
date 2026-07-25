# Build context

## Current milestone

**M02 — Local catalogue and jobs**

## Current work item

**WI-0011 — Add SQLite persistence**

Status: `in_progress`

## Branch and pull request

- Branch: `agent/WI-0011-catalogue-repositories`
- Draft pull request: [#18 — Add typed asset catalogue repositories](https://github.com/erikwasa/Photo-Identity-Indexer/pull/18)

## Objective

Establish versioned SQLite migrations and repositories for the local catalogue, human identity labels, model-derived observations and embeddings, and durable processing records.

## Current slice

Add the typed source, asset and immutable revision repository used by local folder scanning. Source, asset and revision writes are committed atomically, and repeated observations of the same asset/content hash resolve to the existing revision.

## Relevant files

- `src/PhotoIdentity.Persistence.Sqlite/CatalogueRecords.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteAssetCatalogueRepository.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteCatalogueDatabase.cs`
- `tests/PhotoIdentity.Integration.Tests/SqliteAssetCatalogueRepositoryTests.cs`
- `docs/delivery/work-items/WI-0011-sqlite.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test for this slice

- Strong source, asset and revision identifiers round-trip without conversion leaking to callers.
- Source and asset natural-key lookups resolve stable identities for reruns.
- Source, asset and revision rows are written in one transaction.
- Repeated writes for the same asset and SHA-256 return one immutable revision.
- Updated source and asset metadata do not overwrite existing revision history.
- Invalid source/asset/revision relationships are rejected before writing.

## Verification

The schema foundation is complete. Pull request #17 merged at `d1fa036ea256f8d5c9f8133ab184747908f0d64e`, and GitHub Actions run `30173090694` passed restore and vulnerability audit, Release build, all tests, living-document checks and Windows mixed-media verification.

Draft pull request #18 relies on GitHub Actions for executable validation because the agent environment does not contain the .NET SDK.

## Known issues

- This slice covers scanner-facing source, asset and revision persistence only.
- Complete inspection-result transactions, identity records, processing records, backup behaviour and concurrent writer policy remain before WI-0011 is complete.

## Next action

Resolve CI or review findings on pull request #18, then add transactional persistence for complete inspection results: occurrences, observations, crops and embeddings.
