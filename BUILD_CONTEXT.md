# Build context

## Current milestone

**M02 — Local catalogue and jobs**

## Current work item

**WI-0013 — Add resumable batch processing**

Status: `in_progress`

## Branch and pull request

- Branch: `agent/WI-0013-resumable-orchestration`
- Draft pull request: [#24 — Add leased resumable batch orchestration](https://github.com/erikwasa/Photo-Identity-Indexer/pull/24)

## Objective

Add durable attempts, expiring claims, checkpoints, cancellation, bounded retries, idempotency keys and progress summaries so interrupted local batch processing can resume without repeating completed assets.

## Current slice

Establish the safe orchestration foundation before connecting the production inspection pipeline. SQLite owns compare-and-set lease transitions and durable summaries; Core owns neutral processing contracts; the Worker owns retry and resume orchestration; the CLI exposes status and cancellation.

## Relevant files

- `src/PhotoIdentity.Core/Processing/ProcessingContracts.cs`
- `src/PhotoIdentity.Core/Identifiers/EntityIds.cs`
- `src/PhotoIdentity.Persistence.Sqlite/ProcessingRecords.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteProcessingRepository.cs`
- `src/PhotoIdentity.Persistence.Sqlite/SqliteCatalogueDatabase.cs`
- `src/PhotoIdentity.Worker/ResumableBatchProcessor.cs`
- `src/PhotoIdentity.Cli/BatchCommand.cs`
- `tests/PhotoIdentity.Integration.Tests/SqliteProcessingRepositoryTests.cs`
- `tests/PhotoIdentity.Integration.Tests/ResumableBatchProcessorTests.cs`
- `tests/PhotoIdentity.Integration.Tests/BatchCommandTests.cs`
- `docs/delivery/work-items/WI-0013-resumable-processing.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
dotnet test tests/PhotoIdentity.Core.Tests/PhotoIdentity.Core.Tests.csproj
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test for this slice

- Every active job has an expiring lease token.
- Expired work can be reclaimed with a new token.
- A stale worker cannot checkpoint, complete or fail a reclaimed or cancelled job.
- Checkpoints survive interruption and extend the active lease.
- Completed jobs are not repeated after restart; only the interrupted active job may be attempted again.
- Transient failures use bounded retries while permanent failures become terminal immediately.
- Cancellation atomically invalidates active leases and cancels unfinished work.
- Stable idempotency keys survive attempts and migration.
- A synthetic 500-job run produces a complete durable status summary.
- Schema-version-one and version-two databases upgrade transactionally to schema version three.

## Verification

WI-0012 completed in pull request #23 at `5ac2b8263a7b0d82b7a3e23d9dfb676733cc702a`; GitHub Actions run `30179785787` passed dependency audit, Release build, all tests, living-document validation, generated-document checks and Windows mixed-media verification.

Draft pull request #24 relies on GitHub Actions for executable validation because this agent environment does not contain the .NET SDK.

## Known issues

- The orchestration engine is not yet connected to the production inspect pipeline or its transactional face-result repository.
- The 500-item acceptance coverage is synthetic; a real 500-photo folder remains required before WI-0013 completes.
- The current host runs until idle rather than as a long-running service with a defined shutdown loop.
- Cancellation prevents stale database transitions, but concrete handlers must use the supplied idempotency key for external artefact writes.

## Next action

Resolve CI or review findings on pull request #24, then add the production inspection job handler and local batch start/resume entry point.
