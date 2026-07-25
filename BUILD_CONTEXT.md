# Build context

## Current milestone

**M02 — Local catalogue and jobs**

## Current work item

**WI-0011 — Add SQLite persistence**

Status: `in_review`

## Branch and pull request

- Branch: `agent/WI-0011-operational-policy`
- Draft pull request: [#22 — Document SQLite operational policy](https://github.com/erikwasa/Photo-Identity-Indexer/pull/22)

## Objective

Establish versioned SQLite migrations and repositories for the local catalogue, human identity labels, model-derived observations and embeddings, durable processing records, and a safe operational policy.

## Current slice

Document the supported backup and restore path, concurrent-writer boundary, transient-lock handling, abandoned-claim limitation and forward-only schema-upgrade policy needed to complete WI-0011.

## Relevant files

- `docs/operations/sqlite-persistence.md`
- `docs/delivery/work-items/WI-0011-sqlite.md`
- `docs/delivery/status/work-items.yaml`
- `docs/delivery/status/current.md`
- `docs/index.md`
- `README.md`

## Commands

```powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

## Acceptance test for this slice

- The supported backup is a quiesced file copy verified with SQLite integrity, foreign-key and schema-version checks.
- Live-file copying and network-share catalogue locations are explicitly unsupported.
- Restore preserves the previous database for diagnostics and verifies the restored file before processing resumes.
- Repository transactions remain short and do not span decoding, inference, file copying or user interaction.
- Sustained lock failures are surfaced; orchestration may use bounded retries with jitter rather than unbounded retries.
- Abandoned running jobs remain an explicit WI-0013 recovery concern because claims do not yet expire.
- Released migration versions are immutable and future upgrades are forward-only, transactional and backed up first.
- Database backups are documented as separate from source photos and externally stored aligned crops.

## Verification

The schema foundation merged in pull request #17 at `d1fa036ea256f8d5c9f8133ab184747908f0d64e`; GitHub Actions run `30173090694` passed.

The typed asset catalogue repository merged in pull request #18 at `2de0194b0835a8c9b8d13f08b6fa5311e855f889`; GitHub Actions run `30173892338` passed.

The transactional face inspection repository merged in pull request #19 at `382011588f7055d783a0eae4d567f4bbc0adc0c9`; GitHub Actions run `30174420996` passed.

The identity and human-label repository merged in pull request #20 at `e4c2d1311a18b53b1492523789385b65edc9a7fc`; GitHub Actions run `30176173088` passed.

The durable processing repository merged in pull request #21 at `9a3ca9f869b1ae1ae6c09fa4f49130bfd8a832c6`; GitHub Actions run `30176700097` passed restore and vulnerability audit, Release build, all tests, living-document checks and Windows mixed-media verification.

Draft pull request #22 relies on GitHub Actions for executable validation because this agent environment does not contain the .NET SDK.

## Known issues

- Job claims do not use expiring leases; WI-0013 must define abandoned-claim recovery before automatic requeueing.
- Cancellation transitions are represented in typed records but are deferred until the batch orchestration policy is defined.
- Online backup is not exposed by the adapter; only quiesced backups are supported today.
- WAL mode and an application-level busy retry loop are not enabled; the operational policy therefore requires short transactions and bounded orchestration-level retries.

## Next action

Resolve CI or review findings on pull request #22. After it is merged and human verified, mark WI-0011 completed and begin WI-0012 local folder scanning.