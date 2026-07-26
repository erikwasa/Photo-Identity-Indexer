---
id: WI-0013
title: Add resumable batch processing
milestone: M02
status_source: ../status/work-items.yaml
depends_on: [WI-0010, WI-0011, WI-0012]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Cli, PhotoIdentity.Worker, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Integration.Tests]
---

# WI-0013: Add resumable batch processing

## Objective

Add durable jobs, attempts, checkpoints, cancellation, bounded retries and idempotency keys for local batch processing.

## Acceptance criteria

- [x] A stopped run resumes without duplicating completed results.
- [x] At most the active asset is repeated after interruption.
- [x] Transient and permanent failures are separated.
- [ ] A 500-photo sample produces a status summary.

The first three criteria are covered by durable orchestration integration tests. A synthetic 500-job run validates summary scalability and idempotence, but the final criterion remains open until the production inspection handler processes a real 500-photo folder.

## Core processing contract

Core defines infrastructure-neutral orchestration types:

- `ProcessingLeaseToken` is a strong identifier for one active attempt;
- `ProcessingJobContext` supplies the run, job, immutable asset revision, attempt number, stable idempotency key and latest checkpoint;
- `IProcessingJobHandler` executes one asset without knowing SQLite details;
- `IProcessingCheckpointWriter` persists restart state during a long attempt;
- `ProcessingJobFailureException` classifies transient and permanent failures;
- `ProcessingRetryPolicy` provides bounded, capped exponential retry delays.

## Schema version 3

Schema version 3 extends processing records with:

- cancellation request timestamps;
- stable job idempotency keys;
- lease tokens and expiry timestamps;
- JSON checkpoints;
- the most recent failure classification.

Version-one and version-two databases upgrade transactionally. Existing job keys are backfilled from the run and immutable revision identifiers. Legacy `running` jobs are returned to the queue as transiently interrupted work because they predate lease tokens and cannot safely prove ownership.

## Lease and transition policy

`SqliteProcessingRepository` uses lease-token compare-and-set transitions:

- a due queued job receives a new token and expiry;
- an expired active job can be reclaimed with a different token;
- the previous token cannot checkpoint, renew, complete or fail the reclaimed job;
- checkpoints extend the lease and remain available to the next attempt;
- retry scheduling is allowed only for transient failures;
- cancellation marks the run and all unfinished jobs cancelled in one transaction and invalidates active tokens;
- progress summaries report state counts, aggregate attempts and the next due time.

These rules prevent stale workers from changing durable state after cancellation or another worker has reclaimed the attempt. Concrete handlers must also use the supplied idempotency key when writing external artefacts.

## Worker orchestration

`ResumableBatchProcessor` claims and executes jobs until the run is terminal, no work is currently due or an invocation limit is reached. It:

- passes the latest checkpoint and stable idempotency key to the handler;
- applies the configured bounded retry policy to transient failures;
- makes permanent and exhausted failures terminal;
- allows an interrupted lease to expire so another invocation can resume it;
- finalizes the run after every job is terminal.

Integration coverage interrupts a three-job run after the first job succeeds and the second writes a checkpoint. After lease expiry, a new processor does not repeat the first job, retries the active second job and processes the remaining queued job once.

## Operator commands

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  batch status --database C:\PhotoIdentity\catalogue.db --run RUN_ID

dotnet run --project src/PhotoIdentity.Cli -- `
  batch cancel --database C:\PhotoIdentity\catalogue.db --run RUN_ID
```

The status command reports total, queued, running, succeeded, failed and cancelled jobs, aggregate attempts and the next retry time. Cancellation invalidates active leases before returning the updated summary.

## Validation

```powershell
dotnet test tests/PhotoIdentity.Core.Tests/PhotoIdentity.Core.Tests.csproj
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

Draft pull request [#24](https://github.com/erikwasa/Photo-Identity-Indexer/pull/24) adds the leased orchestration foundation and synthetic 500-job status coverage.

## Remaining work

- Connect `IProcessingJobHandler` to the production decode, detection, crop, alignment, embedding and transactional face-persistence path.
- Add a local batch command that creates or resumes a run from catalogued revisions.
- Define the host shutdown loop and lease-renewal cadence for long-running execution.
- Process a real 500-photo sample and retain its privacy-safe status summary as completion evidence.
