# Build context

## Current milestone

**M02 — Local catalogue and jobs**

## Current work item

**WI-0011 — Add SQLite persistence**

Status: `in_progress`

## Branch and pull request

- Branch: `agent/WI-0011-processing-persistence`
- Draft pull request: [#21 — Add durable processing run and job persistence](https://github.com/erikwasa/Photo-Identity-Indexer/pull/21)

## Objective

Establish versioned SQLite migrations and repositories for the local catalogue, human identity labels, model-derived observations and embeddings, and durable processing records.

## Current slice

Add the durable processing boundary for creating runs and queued asset-revision jobs, atomically claiming due work, recording attempts, scheduling retries and finalizing terminal outcomes.

## Relevant files

- `src/PhotoIdentity.Persistence.Sqlite/ProcessingRecords.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteProcessingRepository.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteCatalogueDatabase.cs`
- `tests/PhotoIdentity.Integration.Tests/SqliteProcessingRepositoryTests.cs`
- `docs/delivery/work-items/WI-0011-sqlite.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test for this slice

- Strong run, job and asset-revision identifiers round-trip without conversion leaking to callers.
- A run and its queued jobs are committed in one transaction.
- A missing asset revision rolls back the run and every job.
- Repeated run creation and run/revision pairs retain existing durable rows.
- Only due queued jobs can be claimed.
- Claiming increments the attempt count and moves the run to running.
- Delayed retries preserve attempt history and are unavailable before their retry time.
- Only running jobs can transition to success or failure.
- Concurrent workers claim distinct jobs.
- A run cannot complete while jobs remain queued or running.
- Failed jobs produce a failed terminal run.

## Verification

The schema foundation merged in pull request #17 at `d1fa036ea256f8d5c9f8133ab184747908f0d64e`; GitHub Actions run `30173090694` passed.

The typed asset catalogue repository merged in pull request #18 at `2de0194b0835a8c9b8d13f08b6fa5311e855f889`; GitHub Actions run `30173892338` passed.

The transactional face inspection repository merged in pull request #19 at `382011588f7055d783a0eae4d567f4bbc0adc0c9`; GitHub Actions run `30174420996` passed.

The identity and human-label repository merged in pull request #20 at `e4c2d1311a18b53b1492523789385b65edc9a7fc`; GitHub Actions run `30176173088` passed restore and vulnerability audit, Release build, all tests, living-document checks and Windows mixed-media verification.

Draft pull request #21 relies on GitHub Actions for executable validation because the agent environment does not contain the .NET SDK.

## Known issues

- Job claims do not yet use expiring leases; a worker that dies after claiming requires orchestration-level recovery in WI-0013.
- Cancellation transitions are represented in the typed records but are not exposed until the batch orchestration policy is defined.
- Backup behaviour, long-running concurrent writer policy and future schema upgrades remain before WI-0011 is complete.

## Next action

Resolve CI or review findings on pull request #21, then document the operational SQLite policy and complete WI-0011.
