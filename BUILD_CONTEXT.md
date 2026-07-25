# Build context

## Current milestone

**M02 — Local catalogue and jobs**

## Current work item

**WI-0011 — Add SQLite persistence**

Status: `in_progress`

## Branch and pull request

- Branch: `agent/WI-0011-identity-persistence`
- Draft pull request: [#20 — Add identity and human-label persistence](https://github.com/erikwasa/Photo-Identity-Indexer/pull/20)

## Objective

Establish versioned SQLite migrations and repositories for the local catalogue, human identity labels, model-derived observations and embeddings, and durable processing records.

## Current slice

Add the identity persistence boundary for people, authoritative human labels and separately versioned model suggestions. Person-plus-label writes are transactional, repeated labels keep a stable row identity, and suggestion reruns do not overwrite reviewed status.

## Relevant files

- `src/PhotoIdentity.Persistence.Sqlite/IdentityCatalogueRecords.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteIdentityCatalogueRepository.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteFaceCatalogueRepository.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteCatalogueDatabase.cs`
- `tests/PhotoIdentity.Integration.Tests/SqliteIdentityCatalogueRepositoryTests.cs`
- `docs/delivery/work-items/WI-0011-sqlite.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test for this slice

- Strong person, face occurrence and model identifiers round-trip without conversion leaking to callers.
- A new person and human label are committed in one transaction.
- A missing occurrence rolls back the person and label together.
- Human labels can be stored without detector observations, crops, embeddings or suggestions.
- Repeated person/occurrence/label-kind assignments keep one stable label row and refresh reviewer metadata.
- Suggestions are versioned by occurrence, person, model ID and model hash.
- A rerun refreshes a suggestion score without overwriting reviewed status or original creation time.
- Person merge targets round-trip and self-merges are rejected before writing.

## Verification

The schema foundation merged in pull request #17 at `d1fa036ea256f8d5c9f8133ab184747908f0d64e`; GitHub Actions run `30173090694` passed.

The typed asset catalogue repository merged in pull request #18 at `2de0194b0835a8c9b8d13f08b6fa5311e855f889`; GitHub Actions run `30173892338` passed.

The transactional face inspection repository merged in pull request #19 at `382011588f7055d783a0eae4d567f4bbc0adc0c9`; GitHub Actions run `30174420996` passed restore and vulnerability audit, Release build, all tests, living-document checks and Windows mixed-media verification.

Draft pull request #20 relies on GitHub Actions for executable validation because the agent environment does not contain the .NET SDK.

## Known issues

- Label kinds, assignee identifiers and suggestion statuses remain validated extensible strings until the review workflow defines a closed vocabulary.
- Processing records, backup behaviour and the concurrent writer policy remain before WI-0011 is complete.

## Next action

Resolve CI or review findings on pull request #20, then add typed processing-run and processing-job repositories with concurrency coverage.
