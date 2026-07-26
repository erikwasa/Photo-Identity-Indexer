# Build context

## Current milestone

**M02 — Local catalogue and jobs**

## Current work item

**WI-0013 — Add resumable batch processing**

Status: `in_progress`

## Branch and pull request

- Branch: `agent/WI-0013-local-batch-inspection`
- Draft pull request: [#25 — Connect local batches to production inspection](https://github.com/erikwasa/Photo-Identity-Indexer/pull/25)

## Objective

Connect durable resumable orchestration to local folder scanning and the production decode, YuNet, alignment, SFace and transactional persistence path.

## Current slice

Add usable `batch start` and `batch resume` commands. A model-agnostic coordinator scans and creates jobs; a production handler validates immutable source revisions, writes deterministic aligned crops, persists face results and checkpoints after every committed face.

## Relevant files

- `src/PhotoIdentity.Persistence.Sqlite/SqliteLocalBatchRepository.cs`
- `src/PhotoIdentity.Worker/LocalBatchProcessing.cs`
- `src/PhotoIdentity.Worker/LocalInspectionJobHandler.cs`
- `src/PhotoIdentity.Worker/ResumableBatchProcessor.cs`
- `src/PhotoIdentity.Cli/BatchCommand.cs`
- `src/PhotoIdentity.Cli/Program.cs`
- `tests/PhotoIdentity.Integration.Tests/LocalBatchInspectionTests.cs`
- `docs/delivery/work-items/WI-0013-resumable-processing.md`
- `docs/delivery/status/work-items.yaml`

## Commands

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test for this slice

- The same canonical local root reuses its persisted source identity.
- Start scans supported files and creates one durable job per current non-deleted revision.
- Unsupported files are reported separately from processing failures.
- A partial invocation resumes the same run without repeating completed revisions.
- Resume reconstructs the saved source, output and model configuration.
- The handler rejects path traversal and source content that no longer matches the immutable revision.
- Face order, crop paths and result paths are deterministic for a run and revision.
- Each face transaction commits before its checkpoint is advanced.
- Retrying from a face checkpoint does not duplicate occurrences, observations, crops or embeddings.
- Batch output cannot be placed below the scanned source root.
- The existing leased orchestration, cancellation and synthetic 500-job tests continue to pass.

## Verification

WI-0012 completed in pull request #23 at `5ac2b8263a7b0d82b7a3e23d9dfb676733cc702a`; GitHub Actions run `30179785787` passed.

The leased WI-0013 orchestration foundation merged in pull request #24 at `d87d1604fe1d958f8bcf5fb023f9dadd14786cb8`; GitHub Actions run `30181221035` passed dependency audit, Release build, all tests, living-document validation, generated-document checks and Windows mixed-media verification.

Draft pull request #25 relies on GitHub Actions for executable validation because this agent environment does not contain the .NET SDK.

## Known issues

- The final 500-photo acceptance criterion requires a private local folder and a privacy-safe retained summary; CI uses synthetic and generated images only.
- Batch output stores aligned crops and compact per-asset result manifests rather than the interactive annotated SVG.
- Changed or missing content fails the immutable revision and requires a new scan/run.
- The host runs until idle rather than as a resident service.

## Next action

Resolve CI or review findings on pull request #25. After merge, run a private 500-photo verification and record only aggregate status evidence before marking WI-0013 completed.
