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

The first three criteria are covered by durable orchestration and production-handler integration tests. A synthetic 500-job run validates summary scalability and idempotence. The final criterion remains open until a private local 500-photo folder is processed and a privacy-safe status summary is retained.

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

These rules prevent stale workers from changing durable state after cancellation or another worker has reclaimed the attempt. Concrete handlers also use deterministic run/revision paths when writing external artefacts.

## Worker orchestration

`ResumableBatchProcessor` claims and executes jobs until the run is terminal, no work is currently due or an invocation limit is reached. It:

- passes the latest checkpoint and stable idempotency key to the handler;
- applies the configured bounded retry policy to transient failures;
- makes permanent and exhausted failures terminal;
- allows an interrupted lease to expire so another invocation can resume it;
- finalizes the run after every job is terminal.

Integration coverage interrupts a three-job run after the first job succeeds and the second writes a checkpoint. After lease expiry, a new processor does not repeat the first job, retries the active second job and processes the remaining queued job once.

## Local batch coordination

`LocalBatchCoordinator` keeps scan and orchestration concerns separate from model execution:

- a stable source row is reused for the same canonical local root;
- the local source is scanned before a run is created;
- one job is created for the latest immutable revision of every non-deleted asset;
- unsupported files are counted separately from processing failures;
- the complete run configuration is stored as JSON and reconstructed by resume;
- output directories below the source root are rejected so generated PNG files cannot become later scan inputs.

## Production inspection handler

`LocalInspectionJobHandler` connects each durable job to the existing OpenCV, YuNet and SFace components:

- the immutable revision is resolved to its source root and stable source key;
- path traversal is rejected and the current file hash must match the catalogued revision;
- the image is decoded, faces are detected in deterministic order, aligned and embedded;
- aligned PNG crops use deterministic run, revision and face paths and are replaced atomically;
- each occurrence, detector observation, crop and embedding is persisted transactionally;
- a checkpoint is written only after the corresponding face transaction commits;
- retrying from the latest checkpoint does not duplicate completed face rows or artefacts;
- missing or changed source content is permanent for that immutable revision and requires a rescan/new run;
- non-file-system I/O failures are eligible for bounded transient retry.

The batch path deliberately stores aligned crops and a compact per-asset result manifest. Annotated SVG output remains specific to the interactive single-image `photoid inspect` command.

## Operator commands

```powershell
dotnet run --project src/PhotoIdentity.Cli -- `
  batch start `
  --database C:\PhotoIdentity\catalogue.db `
  --source C:\Photos `
  --output C:\PhotoIdentity\outputs

dotnet run --project src/PhotoIdentity.Cli -- `
  batch resume --database C:\PhotoIdentity\catalogue.db --run RUN_ID

dotnet run --project src/PhotoIdentity.Cli -- `
  batch status --database C:\PhotoIdentity\catalogue.db --run RUN_ID

dotnet run --project src/PhotoIdentity.Cli -- `
  batch cancel --database C:\PhotoIdentity\catalogue.db --run RUN_ID
```

Start reports scan counts followed by the durable processing summary. Resume reconstructs the saved source, output and model configuration. Status reports total, queued, running, succeeded, failed and cancelled jobs, aggregate attempts and the next retry time. Cancellation invalidates active leases before returning the updated summary.

## Validation

```powershell
dotnet test tests/PhotoIdentity.Core.Tests/PhotoIdentity.Core.Tests.csproj
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

Pull request [#24](https://github.com/erikwasa/Photo-Identity-Indexer/pull/24) merged the leased orchestration foundation at `d87d1604fe1d958f8bcf5fb023f9dadd14786cb8`; GitHub Actions run `30181221035` passed.

Draft pull request [#25](https://github.com/erikwasa/Photo-Identity-Indexer/pull/25) connects the production local inspection handler and start/resume commands.

## Remaining work

- Process a private real 500-photo sample with `batch start` and retain only its privacy-safe status summary as completion evidence.
- Address CI or review findings on pull request #25 before requesting human verification.
